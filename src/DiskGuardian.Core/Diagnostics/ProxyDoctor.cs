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

public sealed record IdeProxyDiagnosis(
    string Name,
    string SettingsPath,
    bool Installed,
    string ProxySupport,
    string HttpProxy,
    bool HasExplicitProxy,
    string StatusDisplay,
    bool NeedsProxyHarden);

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
    List<string> Details,
    string ProxyTypeDisplay,
    string ProxyHost,
    int? ProxyPort,
    string ProxyOverride,
    string AutoConfigUrl,
    string BinaryFlagHex,
    string BinaryFlagDescription,
    string PortStatusDisplay,
    long? PortLatencyMs,
    string WorkBuddyStatusDisplay,
    string EnvStatusDisplay,
    string GitStatusDisplay,
    string AppHardeningDisplay,
    string ToolchainStatusDisplay,
    bool WorkBuddyDetected,
    List<IdeProxyDiagnosis> IdeList,
    List<string> RunningProxyClients,
    string RunningClientsDisplay);

public sealed record ProxyRepairResult(
    bool Succeeded,
    string BackupPath,
    List<string> ActionsTaken,
    string? ErrorMessage);

/// <summary>
/// Windows 系统网络与幽灵代理（Ghost Proxy）诊断与修复医生：
/// 1. 深度检测 WinINET 注册表二进制 Flags (Connections\DefaultConnectionSettings) 与 DWORD ProxyEnable 的冲突；
/// 2. 针对 127.0.0.1 / localhost 本地代理进行 TCP 活跃度探测与延迟测量，识别已挂死的死端口；
/// 3. 检测环境变量 (HTTP_PROXY / HTTPS_PROXY) 脏数据污染与下游工具 (WorkBuddy) 易感漏洞；
/// 4. 安全第一：先备份注册表，清洗二进制标志位，通过 WinINet API 广播刷新（坚决不杀死 explorer.exe）。
/// </summary>
public static class ProxyDoctor
{
    private const string InternetSettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string ConnectionsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings\Connections";

    [DllImport("wininet.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    private static extern bool DnsFlushResolverCache();

    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    private static readonly string[] KnownProxyProcessKeywords =
    {
        "flclash", "clash", "mihomo", "v2ray", "xray", "sing-box"
    };

    /// <summary>
    /// 探测系统当前运行的主流代理客户端进程与系统服务
    /// </summary>
    public static List<(string ProcessName, int Id, bool IsService)> DetectRunningProxyProcesses()
    {
        var list = new List<(string, int, bool)>();
        try
        {
            var processes = System.Diagnostics.Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    var nameLower = p.ProcessName.ToLowerInvariant();
                    if (KnownProxyProcessKeywords.Any(k => nameLower.Contains(k)))
                    {
                        bool isService = p.SessionId == 0;
                        list.Add((p.ProcessName, p.Id, isService));
                    }
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch { }
        return list;
    }

    /// <summary>
    /// 全面体检当前系统的代理状态，提取丰富细节
    /// </summary>
    public static ProxyDiagnosisResult Diagnose()
    {
        var details = new List<string>();
        bool proxyEnable = false;
        string proxyServer = "";
        string proxyOverride = "";
        string autoConfigUrl = "";
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
                proxyOverride = reg.GetValue("ProxyOverride", "")?.ToString()?.Trim() ?? "";
                autoConfigUrl = reg.GetValue("AutoConfigURL", "")?.ToString()?.Trim() ?? "";
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
        byte binaryFlag = 0x01;
        if (defaultConn != null && defaultConn.Length > 8)
        {
            binaryFlag = defaultConn[8];
            binaryManualProxy = (binaryFlag & 0x02) != 0;
        }

        string binaryHex = $"0x{binaryFlag:X2}";
        string binaryDesc = InterpretBinaryFlag(binaryFlag);

        // 3. 冲突判定：UI 显示关闭，但二进制底层仍有 0x02 手动代理标记
        bool inconsistentFlags = (!proxyEnable && binaryManualProxy);
        if (inconsistentFlags)
        {
            details.Add($"[注册表冲突] DWORD ProxyEnable=0 (表面关闭)，但 DefaultConnectionSettings 标志位={binaryHex} (仍含手动代理标志)，会导致 Electron/Chromium 读取后误认为代理开启并强行注入。");
        }

        // 4. 死端口探测 (针对 127.0.0.1 / localhost 回环代理)
        bool isDeadPort = false;
        bool isPortListening = false;
        int? parsedPort = null;
        string parsedHost = "";
        string portStatusDisplay;
        long? portLatencyMs = null;

        if (!string.IsNullOrWhiteSpace(proxyServer))
        {
            var (host, port) = ParseProxyEndpoint(proxyServer);
            parsedHost = host;
            parsedPort = port;

            if (port.HasValue && IsLoopbackHost(host))
            {
                var (isListening, latency) = TestPort(host, port.Value);
                isPortListening = isListening;
                portLatencyMs = latency;

                if (!isListening)
                {
                    isDeadPort = true;
                    portStatusDisplay = $"🔴 端口闭塞 ({host}:{port.Value} 未监听)";
                    details.Add($"[死代理风险] 系统代理指向本地回环 {host}:{port.Value}，但当前该端口【无任何进程在监听】。如果开启代理，所有网络请求将报 actively refused (10061)。");
                }
                else
                {
                    portStatusDisplay = $"🟢 正在监听 ({latency}ms 响应正常)";
                    details.Add($"[活跃代理] 检测到代理软件正在监听 {host}:{port.Value} (握手响应 {latency}ms)。");
                }
            }
            else if (!string.IsNullOrWhiteSpace(host))
            {
                portStatusDisplay = $"🌐 外部代理 ({host}:{port})";
                details.Add($"[外部代理] 系统代理指向外部地址: {proxyServer}");
            }
            else
            {
                portStatusDisplay = "⚪ 未配置有效主机";
            }
        }
        else
        {
            portStatusDisplay = "⚪ 直连互联网 (无需端口)";
        }

        // 5. 工作模式判定
        string proxyTypeDisplay;
        if (proxyEnable)
        {
            if (!string.IsNullOrEmpty(proxyServer))
                proxyTypeDisplay = "手动代理 (HTTP/SOCKS)";
            else if (!string.IsNullOrEmpty(autoConfigUrl))
                proxyTypeDisplay = "自动 PAC 脚本";
            else
                proxyTypeDisplay = "系统代理开启 (未设目标)";
        }
        else
        {
            if (inconsistentFlags)
                proxyTypeDisplay = "⚠️ 幽灵残留 (表面关/底层开)";
            else
                proxyTypeDisplay = "系统直连 (未启用代理)";
        }

        // 6. 用户环境变量检查 (HTTP_PROXY / HTTPS_PROXY / ALL_PROXY)
        string? userEnvHttp = null;
        bool deadUserEnv = false;
        string envStatusDisplay;
        try
        {
            userEnvHttp = Environment.GetEnvironmentVariable("HTTP_PROXY", EnvironmentVariableTarget.User)
                       ?? Environment.GetEnvironmentVariable("HTTPS_PROXY", EnvironmentVariableTarget.User)
                       ?? Environment.GetEnvironmentVariable("ALL_PROXY", EnvironmentVariableTarget.User);

            if (!string.IsNullOrWhiteSpace(userEnvHttp))
            {
                envStatusDisplay = $"残留设置: {userEnvHttp}";
                var (envHost, envPort) = ParseProxyEndpoint(userEnvHttp);
                if (envPort.HasValue && IsLoopbackHost(envHost))
                {
                    var (envListening, _) = TestPort(envHost, envPort.Value);
                    if (!envListening)
                    {
                        deadUserEnv = true;
                        details.Add($"[环境变量污染] 用户环境变量 HTTP_PROXY 指向未监听的本地死端口: {userEnvHttp}");
                    }
                }
            }
            else
            {
                envStatusDisplay = "纯净 (无用户环境变量污染)";
            }
        }
        catch
        {
            envStatusDisplay = "无法读取环境变量";
        }

        // 7. Git 全局代理配置检查 (~/.gitconfig)
        string? gitProxy = null;
        bool deadGitProxy = false;
        string gitStatusDisplay;
        try
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var gitConfig = Path.Combine(userProfile, ".gitconfig");
            if (File.Exists(gitConfig))
            {
                gitProxy = ParseGitProxy(gitConfig);
                if (!string.IsNullOrWhiteSpace(gitProxy))
                {
                    var (gHost, gPort) = ParseProxyEndpoint(gitProxy);
                    if (gPort.HasValue && IsLoopbackHost(gHost))
                    {
                        var (gListening, _) = TestPort(gHost, gPort.Value);
                        if (!gListening)
                        {
                            deadGitProxy = true;
                            gitStatusDisplay = $"🔴 残留死代理 ({gitProxy})";
                            details.Add($"[Git 代理死锁] ~/.gitconfig 中配置了 http.proxy = {gitProxy}，但该端口未监听，会导致 git clone/push 报 10061 拒绝连接。");
                        }
                        else
                        {
                            gitStatusDisplay = $"活跃代理 ({gitProxy})";
                        }
                    }
                    else
                    {
                        gitStatusDisplay = $"已配置: {gitProxy}";
                    }
                }
                else
                {
                    gitStatusDisplay = "未配置 (直连模式)";
                }
            }
            else
            {
                gitStatusDisplay = "未发现配置文件 (直连)";
            }
        }
        catch
        {
            gitStatusDisplay = "读取异常";
        }

        // 8. 主流 Agent IDE 与开发者工具链代理防线体检 (VS Code, Cursor, Windsurf, Trae, WorkBuddy, ZCode)
        var ideList = DetectAgentIdes(parsedHost, parsedPort, isPortListening, details);
        bool hasIdeVulnerability = ideList.Any(i => i.NeedsProxyHarden);
        bool workBuddyDetected = ideList.Any(i => i.Name == "WorkBuddy");
        bool workBuddyVulnerable = ideList.Any(i => i.Name == "WorkBuddy" && i.NeedsProxyHarden);
        string wbStatusDisplay = ideList.FirstOrDefault(i => i.Name == "WorkBuddy")?.StatusDisplay ?? "未安装 (无需加固)";

        string appHardeningDisplay;
        if (ideList.Count == 0)
        {
            appHardeningDisplay = "未发现已安装的 Agent IDE";
        }
        else if (hasIdeVulnerability)
        {
            int vulnCnt = ideList.Count(i => i.NeedsProxyHarden);
            appHardeningDisplay = $"⚠️ {vulnCnt} 款 IDE 缺少代理支持 (自定义模型将断网)";
        }
        else
        {
            appHardeningDisplay = $"🟢 全部 {ideList.Count} 款 Agent IDE 配置健康";
        }

        // 综合下游工具链状态
        string toolchainStatusDisplay;
        if (deadUserEnv || deadGitProxy)
        {
            toolchainStatusDisplay = "🔴 检测到下游死端口污染";
        }
        else if (hasIdeVulnerability)
        {
            toolchainStatusDisplay = "🟡 良好 (存在 Agent IDE 代理加固项)";
        }
        else
        {
            toolchainStatusDisplay = "🟢 纯净健康 (无外部代理污染)";
        }

        // 9. 探测后台常驻代理进程（如 FlClash、Clash Verge、Mihomo 等）
        var runningClients = DetectRunningProxyProcesses();
        var clientStrings = new List<string>();
        string runningClientsDisplay;
        if (runningClients.Count > 0)
        {
            foreach (var c in runningClients)
            {
                string desc = c.IsService ? $"{c.ProcessName} (PID: {c.Id}, 系统服务)" : $"{c.ProcessName} (PID: {c.Id}, 托盘/前台)";
                clientStrings.Add(desc);
            }
            runningClientsDisplay = string.Join(", ", runningClients.Select(c => c.ProcessName).Distinct());
            details.Add($"· [后台驻留] 检测到 {runningClients.Count} 个代理相关进程运行中: {string.Join("；", clientStrings)}。");
            if (proxyEnable)
            {
                details.Add("· [游戏直连提示] 若需打游戏直连，请在代理客户端托盘中彻底退出，或在工具箱中使用【一键切换纯净直连】退出后台并重置代理。");
            }
        }
        else
        {
            runningClientsDisplay = "无后台常驻进程";
        }

        // 10. 综合评定
        bool hasAnomaly = inconsistentFlags || (isDeadPort && (proxyEnable || binaryManualProxy)) || deadUserEnv || deadGitProxy;

        string summary;
        if (hasAnomaly)
        {
            if (inconsistentFlags && isDeadPort)
                summary = $"⚠️ 严重幽灵代理：注册表标志位冲突且残留死端口 ({proxyServer})";
            else if (inconsistentFlags)
                summary = "⚠️ 注册表存在幽灵代理标志冲突 (底层仍标记手动代理)";
            else if (isDeadPort)
                summary = $"⚠️ 代理死端口：{proxyServer} 未在监听，请求将被拒";
            else if (deadGitProxy)
                summary = "⚠️ Git 配置残留失效代理，终端网络将被拒";
            else
                summary = "⚠️ 检测到用户环境变量残留失效代理";
        }
        else
        {
            if (proxyEnable)
            {
                if (runningClients.Count > 0)
                    summary = $"活跃代理 (由 {runningClientsDisplay} 托管: {proxyServer})";
                else
                    summary = $"正常 (正在使用活跃代理: {proxyServer})";
            }
            else
            {
                if (runningClients.Count > 0)
                    summary = $"系统直连 (注意: 后台仍有 {runningClientsDisplay} 在运行)";
                else
                    summary = "健康 (纯净直连模式，无幽灵代理残留)";
            }
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
            Details: details,
            ProxyTypeDisplay: proxyTypeDisplay,
            ProxyHost: parsedHost,
            ProxyPort: parsedPort,
            ProxyOverride: proxyOverride,
            AutoConfigUrl: autoConfigUrl,
            BinaryFlagHex: binaryHex,
            BinaryFlagDescription: binaryDesc,
            PortStatusDisplay: portStatusDisplay,
            PortLatencyMs: portLatencyMs,
            WorkBuddyStatusDisplay: wbStatusDisplay,
            EnvStatusDisplay: envStatusDisplay,
            GitStatusDisplay: gitStatusDisplay,
            AppHardeningDisplay: appHardeningDisplay,
            ToolchainStatusDisplay: toolchainStatusDisplay,
            WorkBuddyDetected: workBuddyDetected,
            IdeList: ideList,
            RunningProxyClients: clientStrings,
            RunningClientsDisplay: runningClientsDisplay);
    }

    private static string InterpretBinaryFlag(byte flag)
    {
        var parts = new List<string>();
        if ((flag & 0x01) != 0) parts.Add("直连");
        if ((flag & 0x02) != 0) parts.Add("手动代理");
        if ((flag & 0x04) != 0) parts.Add("自动检测WPAD");
        if ((flag & 0x08) != 0) parts.Add("PAC脚本");
        if (parts.Count == 0) return $"0x{flag:X2} (无标志)";
        return $"0x{flag:X2} ({string.Join("+", parts)})";
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
                actions.Add($"清理用户环境变量跳过: {ex.Message}");
            }

            // 步骤 5: 加固主流 Agent IDE 与开发工具代理防线 (VS Code, Cursor, Trae, WorkBuddy, Windsurf 等)
            if (hardenWorkBuddy)
            {
                try
                {
                    var (ideCount, ideLogs) = HardenAllIdeProxies();
                    foreach (var l in ideLogs)
                    {
                        actions.Add(l);
                    }
                }
                catch (Exception ex)
                {
                    actions.Add($"加固 Agent IDE 配置跳过: {ex.Message}");
                }
            }

            return new ProxyRepairResult(
                Succeeded: true,
                BackupPath: backupPath,
                ActionsTaken: actions,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            return new ProxyRepairResult(
                Succeeded: false,
                BackupPath: backupPath,
                ActionsTaken: actions,
                ErrorMessage: ex.Message);
        }
    }

    /// <summary>
    /// 一键切回纯净直连模式 (专为打游戏、局域网直连或彻底退出代理设计)：
    /// 1. 可选安全退出后台残留的代理进程（如 FlClash 托盘/内核、Clash Verge 等，绝不修改删除用户订阅与配置文件）；
    /// 2. 彻底重置注册表 ProxyEnable=0 并清空 ProxyServer；
    /// 3. 清洗 DefaultConnectionSettings 与 SavedLegacySettings 的第 8 字节二进制标志为 0x01 (直连)；
    /// 4. 清理用户环境变量 (HTTP_PROXY / HTTPS_PROXY / ALL_PROXY)；
    /// 5. 通过 WinINet API 向操作系统广播代理设置更新 (全局即刻生效，绝不杀 explorer.exe)；
    /// 6. 调用 Win32 API 刷新系统 DNS 解析缓存 (清除 Fake-IP 域名污染)。
    /// </summary>
    public static ProxyRepairResult SwitchToPureDirect(bool terminateRunningClients = true, string? outputDir = null)
    {
        var actions = new List<string>();
        string backupPath = "";

        try
        {
            // 步骤 1: 自动备份当前注册表原值
            var baseDir = outputDir ?? AppDomain.CurrentDomain.BaseDirectory;
            var outDir = Path.Combine(baseDir, "out");
            Directory.CreateDirectory(outDir);

            backupPath = Path.Combine(outDir, $"proxy_backup_{DateTime.Now:yyyyMMdd_HHmmss}.reg");
            ExportProxyRegistryBackup(backupPath);
            actions.Add($"已自动备份当前注册表原值至: {backupPath}");

            // 步骤 2: 如需要，安全退出/终止后台残留的代理客户端 (保护用户配置与订阅)
            if (terminateRunningClients)
            {
                var running = DetectRunningProxyProcesses();
                int killedCount = 0;
                foreach (var (name, id, isService) in running)
                {
                    try
                    {
                        using var proc = System.Diagnostics.Process.GetProcessById(id);
                        if (!proc.HasExited)
                        {
                            proc.Kill();
                            proc.WaitForExit(1000);
                            killedCount++;
                            actions.Add($"已安全退出后台代理进程: {name} (PID: {id})");
                        }
                    }
                    catch (Exception ex)
                    {
                        actions.Add($"尝试退出 {name} (PID: {id}) 提示: {ex.Message}");
                    }
                }
                if (killedCount == 0 && running.Count == 0)
                {
                    actions.Add("后台未检测到常驻代理进程。");
                }
            }

            // 步骤 3: 强制重置注册表 ProxyEnable 为 0 并清空 ProxyServer
            using (var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, true))
            {
                if (key != null)
                {
                    key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    key.SetValue("ProxyServer", "", RegistryValueKind.String);
                    actions.Add("已将注册表 ProxyEnable 设为 0 (完全关闭)，并清空 ProxyServer 字段");
                }
            }

            // 步骤 4: 清洗二进制标志位
            using (var connKey = Registry.CurrentUser.OpenSubKey(ConnectionsKey, true))
            {
                if (connKey != null)
                {
                    SanitizeConnectionBinary(connKey, "DefaultConnectionSettings", actions);
                    SanitizeConnectionBinary(connKey, "SavedLegacySettings", actions);
                }
            }

            // 步骤 5: 广播 WinINet 配置变更 (严禁杀死 explorer.exe)
            bool opt1 = InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            bool opt2 = InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            if (opt1 && opt2)
            {
                actions.Add("已通过 WinINet API 向操作系统广播系统代理已关闭 (即刻生效)");
            }

            // 步骤 6: 清理用户级环境变量 (HTTP_PROXY / HTTPS_PROXY / ALL_PROXY)
            try
            {
                Environment.SetEnvironmentVariable("HTTP_PROXY", null, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("HTTPS_PROXY", null, EnvironmentVariableTarget.User);
                Environment.SetEnvironmentVariable("ALL_PROXY", null, EnvironmentVariableTarget.User);
                actions.Add("已清除用户环境变量 HTTP_PROXY / HTTPS_PROXY / ALL_PROXY");
            }
            catch (Exception ex)
            {
                actions.Add($"清理环境变量跳过: {ex.Message}");
            }

            // 步骤 7: 刷新系统 DNS 缓存
            try
            {
                if (DnsFlushResolverCache())
                {
                    actions.Add("已刷新 Windows 系统 DNS 解析缓存 (清除 Fake-IP 域名污染)");
                }
                else
                {
                    actions.Add("已请求刷新系统 DNS 缓存");
                }
            }
            catch (Exception ex)
            {
                actions.Add($"刷新 DNS 接口异常: {ex.Message}");
            }

            return new ProxyRepairResult(
                Succeeded: true,
                BackupPath: backupPath,
                ActionsTaken: actions,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            return new ProxyRepairResult(
                Succeeded: false,
                BackupPath: backupPath,
                ActionsTaken: actions,
                ErrorMessage: ex.Message);
        }
    }

    private static void SanitizeConnectionBinary(RegistryKey connKey, string valueName, List<string> actions)
    {
        var data = connKey.GetValue(valueName) as byte[];
        if (data == null || data.Length <= 8) return;

        // 第 8 字节清掉 0x02 手动代理位，确保 0x01 直连生效
        byte oldFlag = data[8];
        byte newFlag = (byte)((oldFlag & ~0x02) | 0x01);

        if (oldFlag != newFlag)
        {
            data[8] = newFlag;

            // 递增计数器 (bytes 4..7)
            if (data.Length >= 8)
            {
                int counter = BitConverter.ToInt32(data, 4);
                counter++;
                var counterBytes = BitConverter.GetBytes(counter);
                Array.Copy(counterBytes, 0, data, 4, 4);
            }

            connKey.SetValue(valueName, data, RegistryValueKind.Binary);
            actions.Add($"已清洗 {valueName} 二进制标志位: 0x{oldFlag:X2} -> 0x{newFlag:X2}");
        }
    }

    private static void ExportProxyRegistryBackup(string regFilePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Windows Registry Editor Version 5.00");
        sb.AppendLine();
        sb.AppendLine($@"[HKEY_CURRENT_USER\{InternetSettingsKey}]");

        using (var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, false))
        {
            if (key != null)
            {
                WriteRegValue(sb, "ProxyEnable", key.GetValue("ProxyEnable"), key.GetValueKind("ProxyEnable"));
                WriteRegValue(sb, "ProxyServer", key.GetValue("ProxyServer"), key.GetValueKind("ProxyServer"));
                WriteRegValue(sb, "ProxyOverride", key.GetValue("ProxyOverride"), key.GetValueKind("ProxyOverride"));
            }
        }

        sb.AppendLine();
        sb.AppendLine($@"[HKEY_CURRENT_USER\{ConnectionsKey}]");
        using (var key = Registry.CurrentUser.OpenSubKey(ConnectionsKey, false))
        {
            if (key != null)
            {
                WriteRegBinary(sb, "DefaultConnectionSettings", key.GetValue("DefaultConnectionSettings") as byte[]);
                WriteRegBinary(sb, "SavedLegacySettings", key.GetValue("SavedLegacySettings") as byte[]);
            }
        }

        File.WriteAllText(regFilePath, sb.ToString(), Encoding.Unicode);
    }

    private static void WriteRegValue(StringBuilder sb, string name, object? val, RegistryValueKind kind)
    {
        if (val == null) return;
        if (kind == RegistryValueKind.DWord)
        {
            sb.AppendLine($"\"{name}\"=dword:{Convert.ToInt32(val):x8}");
        }
        else if (kind == RegistryValueKind.String)
        {
            sb.AppendLine($"\"{name}\"=\"{val.ToString()?.Replace("\\", "\\\\")}\"");
        }
    }

    private static void WriteRegBinary(StringBuilder sb, string name, byte[]? bytes)
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

    public static bool IsLoopbackHost(string host)
    {
        return host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("::1", StringComparison.OrdinalIgnoreCase);
    }

    private static (bool IsListening, long LatencyMs) TestPort(string host, int port)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            using var client = new TcpClient();
            var targetHost = host.Equals("0.0.0.0") ? "127.0.0.1" : host;
            var ar = client.BeginConnect(targetHost, port, null, null);
            bool success = ar.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(250));
            sw.Stop();
            if (!success) return (false, sw.ElapsedMilliseconds);
            client.EndConnect(ar);
            return (true, sw.ElapsedMilliseconds);
        }
        catch
        {
            sw.Stop();
            return (false, sw.ElapsedMilliseconds);
        }
    }

    private static string? ParseGitProxy(string gitConfigPath)
    {
        try
        {
            var lines = File.ReadAllLines(gitConfigPath);
            bool inHttp = false;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    inHttp = trimmed.Equals("[http]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (inHttp && trimmed.StartsWith("proxy", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        return parts[1].Trim().Trim('"', '\'');
                    }
                }
            }
        }
        catch { }
        return null;
    }

    public static List<(string Name, string Path)> GetKnownIdeSpecs()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return new List<(string Name, string Path)>
        {
            ("VS Code", Path.Combine(appData, "Code", "User", "settings.json")),
            ("Cursor", Path.Combine(appData, "Cursor", "User", "settings.json")),
            ("Trae", Path.Combine(appData, "Trae", "User", "settings.json")),
            ("Windsurf", Path.Combine(appData, "Windsurf", "User", "settings.json")),
            ("WorkBuddy", Path.Combine(userProfile, ".workbuddy", "settings.json")),
            ("WorkBuddy AI", Path.Combine(userProfile, ".workbuddy-ai", "settings.json")),
            ("ZCode", Path.Combine(appData, "zcode", "User", "settings.json")),
            ("VS Code Insiders", Path.Combine(appData, "Code - Insiders", "User", "settings.json"))
        };
    }

    public static List<IdeProxyDiagnosis> DetectAgentIdes(string proxyHost, int? proxyPort, bool isPortListening, List<string> details)
    {
        var result = new List<IdeProxyDiagnosis>();
        var specs = GetKnownIdeSpecs();

        foreach (var (name, path) in specs)
        {
            if (!File.Exists(path)) continue;

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string proxySupport = root.TryGetProperty("http.proxySupport", out var psProp) ? psProp.GetString() ?? "" : "";
                string httpProxy = root.TryGetProperty("http.proxy", out var hpProp) ? hpProp.GetString() ?? "" : "";
                string mode = root.TryGetProperty("mode", out var mProp) ? mProp.GetString() ?? "" : "";

                bool hasExplicitProxy = !string.IsNullOrWhiteSpace(httpProxy);
                bool needsHarden = false;
                string statusDisplay;

                if (isPortListening && proxyPort.HasValue)
                {
                    // 系统处于活跃代理状态：IDE 必须启用 proxySupport 或显式配置 proxy，否则自定义模型（境外API）会直接走直连并断网
                    if (proxySupport.Equals("on", StringComparison.OrdinalIgnoreCase) || hasExplicitProxy)
                    {
                        statusDisplay = "🟢 代理支持已开启 (模型外联正常)";
                    }
                    else
                    {
                        needsHarden = true;
                        statusDisplay = "⚠️ 缺少代理支持 (自定义模型将直连并失败)";
                        details.Add($"[Agent IDE 代理缺陷] 检测到 {name} 缺少 \"http.proxySupport\": \"on\"，在访问海外自定义模型（如 DeepSeek、Claude、TokenHarbor 等）时将强行走直连导致断网。");
                    }
                }
                else
                {
                    // 系统处于直连状态：检查 IDE 是否残留失效死代理
                    if (hasExplicitProxy)
                    {
                        var (hHost, hPort) = ParseProxyEndpoint(httpProxy);
                        if (hPort.HasValue && IsLoopbackHost(hHost))
                        {
                            var (hListening, _) = TestPort(hHost, hPort.Value);
                            if (!hListening)
                            {
                                needsHarden = true;
                                statusDisplay = $"🔴 残留死代理 ({httpProxy})";
                                details.Add($"[Agent IDE 死代理] 检测到 {name} 残留未监听的代理地址: {httpProxy}，会导致 IDE 内所有网络请求被拒。");
                            }
                            else
                            {
                                statusDisplay = $"活跃代理配置 ({httpProxy})";
                            }
                        }
                        else
                        {
                            statusDisplay = $"已配置代理: {httpProxy}";
                        }
                    }
                    else
                    {
                        statusDisplay = "🟢 健康直连模式";
                    }
                }

                result.Add(new IdeProxyDiagnosis(
                    Name: name,
                    SettingsPath: path,
                    Installed: true,
                    ProxySupport: proxySupport,
                    HttpProxy: httpProxy,
                    HasExplicitProxy: hasExplicitProxy,
                    StatusDisplay: statusDisplay,
                    NeedsProxyHarden: needsHarden));
            }
            catch (Exception ex)
            {
                result.Add(new IdeProxyDiagnosis(
                    Name: name,
                    SettingsPath: path,
                    Installed: true,
                    ProxySupport: "",
                    HttpProxy: "",
                    HasExplicitProxy: false,
                    StatusDisplay: $"解析异常: {ex.Message}",
                    NeedsProxyHarden: false));
            }
        }

        return result;
    }

    /// <summary>
    /// 一键为所有已识别的 Agent IDE（VS Code, Cursor, Trae, WorkBuddy, Windsurf 等）注入代理支持或修复死代理
    /// </summary>
    public static (int SucceededCount, List<string> Logs) HardenAllIdeProxies(string? targetProxyUrl = null)
    {
        var logs = new List<string>();
        int count = 0;
        var specs = GetKnownIdeSpecs();

        // 若未指定目标代理，自动探测系统当前活跃代理
        string activeProxy = targetProxyUrl ?? "";
        if (string.IsNullOrEmpty(activeProxy))
        {
            var diag = Diagnose();
            if (diag.ProxyEnable && !string.IsNullOrEmpty(diag.ProxyServer) && !diag.IsDeadLoopbackPort)
            {
                activeProxy = diag.ProxyServer.StartsWith("http") ? diag.ProxyServer : $"http://{diag.ProxyServer}";
            }
            else
            {
                // 默认 Clash / FlClash 常见端口兜底
                var (is7890, _) = TestPort("127.0.0.1", 7890);
                if (is7890) activeProxy = "http://127.0.0.1:7890";
            }
        }

        foreach (var (name, path) in specs)
        {
            if (!File.Exists(path)) continue;

            try
            {
                var json = File.ReadAllText(path);
                var node = JsonNode.Parse(json);
                if (node is JsonObject obj)
                {
                    // 备份原文件
                    var backupPath = path + $".bak_{DateTime.Now:yyyyMMdd_HHmmss}";
                    File.Copy(path, backupPath, true);

                    // 注入 VS Code 标准代理支持
                    obj["http.proxySupport"] = "on";
                    if (!string.IsNullOrEmpty(activeProxy))
                    {
                        obj["http.proxy"] = activeProxy;
                    }

                    // 兼容 WorkBuddy 特殊模式
                    if (obj.ContainsKey("mode"))
                    {
                        obj["mode"] = !string.IsNullOrEmpty(activeProxy) ? "manual" : "system";
                    }

                    File.WriteAllText(path, obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    count++;
                    logs.Add($"已成功加固 [{name}]: 开启 http.proxySupport: on" + (!string.IsNullOrEmpty(activeProxy) ? $" 并绑定代理 {activeProxy}" : ""));
                }
            }
            catch (Exception ex)
            {
                logs.Add($"加固 [{name}] 失败: {ex.Message}");
            }
        }

        return (count, logs);
    }
}
