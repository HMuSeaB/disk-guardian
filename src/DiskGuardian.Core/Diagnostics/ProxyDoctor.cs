using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace DiskGuardian.Core.Diagnostics;

public sealed record ProxyDiagnosisResult(
    bool HasAnomaly,
    bool InconsistentFlags,
    bool ProxyEnable,
    string ProxyServer,
    bool IsDeadLoopbackPort,
    int? DeadPort,
    string? UserEnvProxy,
    bool WorkBuddyVulnerable,
    string Summary,
    List<string> Details);

public sealed record ProxyRepairResult(
    bool Succeeded,
    string BackupPath,
    List<string> ActionsTaken,
    string? ErrorMessage);

/// <summary>
/// Windows 系统网络与幽灵代理（Ghost Proxy）诊断与修复医生：
/// 1. 深度检测 WinINET 注册表二进制 Flags (Connections\DefaultConnectionSettings) 与 DWORD ProxyEnable 的冲突；
/// 2. 针对 127.0.0.1 / localhost 本地代理进行 TCP 活跃度探测，识别已挂死的死端口；
/// 3. 检测环境变量 (HTTP_PROXY / HTTPS_PROXY) 脏数据污染与下游工具 (WorkBuddy) 易感漏洞；
/// 4. 安全第一：先备份注册表，清洗二进制标志位，通过 WinINet API 广播刷新（坚决不杀死 explorer.exe）。
/// </summary>
public static class ProxyDoctor
{
    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string ConnectionsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings\Connections";

    [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    /// <summary>
    /// 全面体检当前系统的代理状态
    /// </summary>
    public static ProxyDiagnosisResult Diagnose()
    {
        var details = new List<string>();
        bool proxyEnable = false;
        string proxyServer = "";
        byte[]? defaultConn = null;
        byte[]? legacyConn = null;

        // 1. 读取注册表设置
        try
        {
            using var reg = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, false);
            if (reg != null)
            {
                proxyEnable = Convert.ToInt32(reg.GetValue("ProxyEnable", 0)) != 0;
                proxyServer = reg.GetValue("ProxyServer", "")?.ToString()?.Trim() ?? "";
            }

            using var connReg = Registry.CurrentUser.OpenSubKey(ConnectionsKey, false);
            if (connReg != null)
            {
                defaultConn = connReg.GetValue("DefaultConnectionSettings") as byte[];
                legacyConn = connReg.GetValue("SavedLegacySettings") as byte[];
            }
        }
        catch (Exception ex)
        {
            details.Add($"读取注册表失败: {ex.Message}");
        }

        // 2. 分析二进制标志位
        // 结构分析：第 8 字节为代理模式 Flag (0x01: 直连; 0x02: 手动代理; 0x04: 自动检测; 0x08: PAC)
        bool binaryManualProxy = false;
        byte binaryFlag = 0;
        if (defaultConn != null && defaultConn.Length > 8)
        {
            binaryFlag = defaultConn[8];
            binaryManualProxy = (binaryFlag & 0x02) != 0;
        }

        // 3. 冲突判定：UI 显示关闭，但二进制底层仍有 0x02 手动代理标记
        bool inconsistentFlags = (!proxyEnable && binaryManualProxy);
        if (inconsistentFlags)
        {
            details.Add($"[注册表冲突] DWORD ProxyEnable=0 (已关闭)，但 DefaultConnectionSettings 第 8 字节=0x{binaryFlag:X2} (仍含 0x02 手动代理标志)，容易导致 Electron/Chromium 读取后误认为代理开启。");
        }

        // 4. 死端口探测 (针对 127.0.0.1 / localhost 回环代理)
        bool isDeadPort = false;
        int? parsedPort = null;
        if (!string.IsNullOrWhiteSpace(proxyServer))
        {
            var (host, port) = ParseProxyEndpoint(proxyServer);
            if (port.HasValue && IsLoopbackHost(host))
            {
                parsedPort = port.Value;
                bool isListening = IsPortListening(host, port.Value);
                if (!isListening)
                {
                    isDeadPort = true;
                    details.Add($"[死代理风险] 系统代理指向本地回环 {host}:{port.Value}，但当前该端口【无任何进程在监听】。如果开启代理，所有网络请求将报 actively refused (10061)。");
                }
                else
                {
                    details.Add($"[活跃代理] 检测到代理软件正在监听 {host}:{port.Value}。");
                }
            }
            else if (!string.IsNullOrWhiteSpace(host))
            {
                details.Add($"[外部代理] 系统代理指向外部地址: {proxyServer}");
            }
        }

        // 5. 用户环境变量检查 (HTTP_PROXY / HTTPS_PROXY)
        string? userEnvHttp = null;
        bool deadUserEnv = false;
        try
        {
            userEnvHttp = Environment.GetEnvironmentVariable("HTTP_PROXY", EnvironmentVariableTarget.User)
                       ?? Environment.GetEnvironmentVariable("HTTPS_PROXY", EnvironmentVariableTarget.User)
                       ?? Environment.GetEnvironmentVariable("ALL_PROXY", EnvironmentVariableTarget.User);

            if (!string.IsNullOrWhiteSpace(userEnvHttp))
            {
                var (envHost, envPort) = ParseProxyEndpoint(userEnvHttp);
                if (envPort.HasValue && IsLoopbackHost(envHost))
                {
                    if (!IsPortListening(envHost, envPort.Value))
                    {
                        deadUserEnv = true;
                        details.Add($"[环境变量污染] 用户环境变量 HTTP_PROXY 指向未监听的本地死端口: {userEnvHttp}");
                    }
                }
            }
        }
        catch { }

        // 6. WorkBuddy 易感漏洞检查
        bool workBuddyVulnerable = false;
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var wbSettings = Path.Combine(userProfile, ".workbuddy", "settings.json");
            if (File.Exists(wbSettings))
            {
                var json = File.ReadAllText(wbSettings);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("mode", out var modeProp) || modeProp.GetString() != "none")
                {
                    workBuddyVulnerable = true;
                    details.Add("[下游弱点] WorkBuddy (~/.workbuddy/settings.json) 缺少 \"mode\": \"none\" 配置，在系统代理残留时会被迫注入脏代理。");
                }
            }
        }
        catch { }

        // 7. 综合评定
        bool hasAnomaly = inconsistentFlags || (isDeadPort && (proxyEnable || binaryManualProxy)) || deadUserEnv;

        string summary;
        if (hasAnomaly)
        {
            if (inconsistentFlags && isDeadPort)
                summary = $"⚠️ 严重幽灵代理：注册表标志位冲突且残留死端口 ({proxyServer})";
            else if (inconsistentFlags)
                summary = "⚠️ 注册表存在幽灵代理标志冲突 (底层仍标记手动代理)";
            else if (isDeadPort)
                summary = $"⚠️ 代理死端口：{proxyServer} 未在监听，请求将被拒";
            else
                summary = "⚠️ 检测到用户环境变量残留失效代理";
        }
        else
        {
            if (proxyEnable)
                summary = $"正常 (正在使用活跃代理: {proxyServer})";
            else
                summary = "健康 (直连模式，无幽灵代理残留)";
        }

        return new ProxyDiagnosisResult(
            HasAnomaly: hasAnomaly,
            InconsistentFlags: inconsistentFlags,
            ProxyEnable: proxyEnable,
            ProxyServer: proxyServer,
            IsDeadLoopbackPort: isDeadPort,
            DeadPort: parsedPort,
            UserEnvProxy: userEnvHttp,
            WorkBuddyVulnerable: workBuddyVulnerable,
            Summary: summary,
            Details: details);
    }

    /// <summary>
    /// 安全修复幽灵代理：自动备份、清洗二进制、WinINet广播生效、加固下游工具
    /// </summary>
    public static ProxyRepairResult Repair(bool hardenWorkBuddy = true, string? outputDir = null)
    {
        var actions = new List<string>();
        string backupPath = "";

        try
        {
            // 步骤 1: 自动备份
            var baseDir = outputDir ?? AppDomain.CurrentDomain.BaseDirectory;
            var outDir = Path.Combine(baseDir, "out");
            Directory.CreateDirectory(outDir);

            backupPath = Path.Combine(outDir, $"proxy_backup_{DateTime.Now:yyyyMMdd_HHmmss}.reg");
            ExportProxyRegistryBackup(backupPath);
            actions.Add($"已自动备份当前注册表原值至: {backupPath}");

            // 步骤 2: 清洗注册表与二进制 Flags
            using (var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, true))
            {
                if (key != null)
                {
                    key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    key.SetValue("ProxyServer", "", RegistryValueKind.String);
                    actions.Add("已重置 ProxyEnable 为 0，并清空 ProxyServer 字段");
                }
            }

            using (var connKey = Registry.CurrentUser.OpenSubKey(ConnectionsKey, true))
            {
                if (connKey != null)
                {
                    SanitizeConnectionBinary(connKey, "DefaultConnectionSettings", actions);
                    SanitizeConnectionBinary(connKey, "SavedLegacySettings", actions);
                }
            }

            // 步骤 3: 广播 WinINet 配置变更 (严禁杀死 explorer.exe)
            bool opt1 = InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            bool opt2 = InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            if (opt1 && opt2)
            {
                actions.Add("已通过 WinINet API 向操作系统广播代理设置更新 (全局即刻生效)");
            }

            // 步骤 4: 清理用户级环境变量污染 (如果指向未监听的死端口)
            try
            {
                var userHttp = Environment.GetEnvironmentVariable("HTTP_PROXY", EnvironmentVariableTarget.User);
                if (!string.IsNullOrEmpty(userHttp))
                {
                    Environment.SetEnvironmentVariable("HTTP_PROXY", null, EnvironmentVariableTarget.User);
                    actions.Add($"已清除用户环境变量 HTTP_PROXY ({userHttp})");
                }
                var userHttps = Environment.GetEnvironmentVariable("HTTPS_PROXY", EnvironmentVariableTarget.User);
                if (!string.IsNullOrEmpty(userHttps))
                {
                    Environment.SetEnvironmentVariable("HTTPS_PROXY", null, EnvironmentVariableTarget.User);
                    actions.Add($"已清除用户环境变量 HTTPS_PROXY ({userHttps})");
                }
                var userAll = Environment.GetEnvironmentVariable("ALL_PROXY", EnvironmentVariableTarget.User);
                if (!string.IsNullOrEmpty(userAll))
                {
                    Environment.SetEnvironmentVariable("ALL_PROXY", null, EnvironmentVariableTarget.User);
                    actions.Add($"已清除用户环境变量 ALL_PROXY ({userAll})");
                }
            }
            catch (Exception ex)
            {
                actions.Add($"清理环境变量时产生提示: {ex.Message}");
            }

            // 步骤 5: 加固 WorkBuddy 防线
            if (hardenWorkBuddy)
            {
                try
                {
                    var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    var wbSettings = Path.Combine(userProfile, ".workbuddy", "settings.json");
                    if (File.Exists(wbSettings))
                    {
                        var json = File.ReadAllText(wbSettings, Encoding.UTF8);
                        var node = JsonNode.Parse(json);
                        if (node != null)
                        {
                            node["mode"] = "none";
                            var options = new JsonSerializerOptions { WriteIndented = true };
                            File.WriteAllText(wbSettings, node.ToJsonString(options), Encoding.UTF8);
                            actions.Add("已成功在 WorkBuddy settings.json 中注入 \"mode\": \"none\"，永久防御外部脏代理污染");
                        }
                    }
                }
                catch (Exception ex)
                {
                    actions.Add($"加固 WorkBuddy 时产生提示: {ex.Message}");
                }
            }

            return new ProxyRepairResult(true, backupPath, actions, null);
        }
        catch (Exception ex)
        {
            return new ProxyRepairResult(false, backupPath, actions, ex.Message);
        }
    }

    private static void SanitizeConnectionBinary(RegistryKey key, string valueName, List<string> actions)
    {
        var bytes = key.GetValue(valueName) as byte[];
        if (bytes != null && bytes.Length > 8)
        {
            byte originalFlag = bytes[8];
            // 剥离 0x02 (手动代理位)，确保 0x01 (直连位)
            bytes[8] = (byte)((bytes[8] & ~0x02) | 0x01);

            // 计数器 + 1
            if (bytes.Length >= 8)
            {
                uint counter = BitConverter.ToUInt32(bytes, 4);
                byte[] counterBytes = BitConverter.GetBytes(counter + 1);
                Array.Copy(counterBytes, 0, bytes, 4, 4);
            }

            key.SetValue(valueName, bytes, RegistryValueKind.Binary);
            actions.Add($"已清洗二进制键 {valueName}: Flag 0x{originalFlag:X2} -> 0x{bytes[8]:X2} (纯直连)");
        }
    }

    private static void ExportProxyRegistryBackup(string backupFilePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine();
        sb.AppendLine(@"[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings]");

        using (var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, false))
        {
            if (key != null)
            {
                var pe = key.GetValue("ProxyEnable");
                if (pe != null) sb.AppendLine($"\"ProxyEnable\"=dword:{Convert.ToInt32(pe):x8}");
                var ps = key.GetValue("ProxyServer");
                if (ps != null) sb.AppendLine($"\"ProxyServer\"=\"{ps}\"");
            }
        }

        sb.AppendLine();
        sb.AppendLine(@"[HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Internet Settings\Connections]");

        using (var key = Registry.CurrentUser.OpenSubKey(ConnectionsKey, false))
        {
            if (key != null)
            {
                AppendHexValue(sb, "DefaultConnectionSettings", key.GetValue("DefaultConnectionSettings") as byte[]);
                AppendHexValue(sb, "SavedLegacySettings", key.GetValue("SavedLegacySettings") as byte[]);
            }
        }

        File.WriteAllText(backupFilePath, sb.ToString(), Encoding.Unicode);
    }

    private static void AppendHexValue(StringBuilder sb, string name, byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return;
        sb.Append($"\"{name}\"=hex:");
        for (int i = 0; i < bytes.Length; i++)
        {
            sb.Append($"{bytes[i]:x2}");
            if (i < bytes.Length - 1)
            {
                if ((i + 1) % 25 == 0)
                {
                    sb.AppendLine("\\");
                    sb.Append("  ");
                }
                else
                {
                    sb.Append(",");
                }
            }
        }
        sb.AppendLine();
    }

    private static (string Host, int? Port) ParseProxyEndpoint(string proxyStr)
    {
        try
        {
            var raw = proxyStr.Trim();
            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("socks5://", StringComparison.OrdinalIgnoreCase))
            {
                if (Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                {
                    return (uri.Host, uri.Port > 0 ? uri.Port : null);
                }
            }

            // 处理形如 "127.0.0.1:7890" 或 "http=127.0.0.1:7890;https=127.0.0.1:7890"
            var firstEntry = raw.Split(';')[0].Trim();
            if (firstEntry.Contains('='))
            {
                firstEntry = firstEntry.Split('=')[1].Trim();
            }

            var parts = firstEntry.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[1], out var port))
            {
                return (parts[0].Trim(), port);
            }
        }
        catch { }

        return (proxyStr, null);
    }

    private static bool IsLoopbackHost(string host)
    {
        return host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPortListening(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            var targetHost = host.Equals("0.0.0.0") ? "127.0.0.1" : host;
            var ar = client.BeginConnect(targetHost, port, null, null);
            bool success = ar.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(250));
            if (!success) return false;
            client.EndConnect(ar);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
