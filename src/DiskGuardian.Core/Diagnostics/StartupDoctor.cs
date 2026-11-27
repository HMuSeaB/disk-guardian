using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DiskGuardian.Core.Diagnostics;

public sealed class StartupItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "RegistryRun"; // RegistryRun, StartupFolder, AutoService, ScheduledTask, WinsockNsp, AppInitDll, ShellOverlay
    public string Location { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string RawKeyPath { get; set; } = string.Empty;
    public string ApprovedSubKey { get; set; } = string.Empty;
    public bool IsHklm { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsThirdParty { get; set; } = true;
    public string RiskLevel { get; set; } = "Caution"; // Protected, Caution, SafeToDisable
    public string Reason { get; set; } = string.Empty;

    public bool CanToggle =>
        RiskLevel != "Protected" &&
        (Category == "RegistryRun" || Category == "StartupFolder" || Category == "AutoService" || Category == "ScheduledTask");

    public string CategoryDisplay => Category switch
    {
        "RegistryRun" => "🔑 注册表自启",
        "StartupFolder" => "📁 启动文件夹",
        "AutoService" => "⚙️ 自动服务",
        "ScheduledTask" => "⏰ 计划任务",
        "WinsockNsp" => "🌐 Winsock NSP",
        "AppInitDll" => "💉 AppInit_DLLs",
        "ShellOverlay" => "🏷️ Explorer 角标",
        _ => Category
    };

    public string RiskDisplay => RiskLevel switch
    {
        "Protected" => "🛡️ 核心保护",
        "SafeToDisable" => "💡 可优化/冗余",
        _ => "⚠️ 按需保留"
    };

    public string StatusDisplay => IsEnabled ? "✅ 已启用" : "⏸️ 已禁用";
}

public sealed class StartupReport
{
    public int RegistryAndFolderCount { get; set; }
    public int AutoServiceCount { get; set; }
    public int ScheduledTaskCount { get; set; }
    public int ThirdPartyNspCount { get; set; }
    public int AppInitCount { get; set; }
    public int ShellOverlayCount { get; set; }
    public bool IsOverlayBloated => ShellOverlayCount > 15;

    public int TotalItems => Items.Count;
    public int OptimizableCount => Items.Count(i => i.RiskLevel == "SafeToDisable" && i.IsEnabled);
    public string SummaryMessage { get; set; } = string.Empty;
    public List<StartupItem> Items { get; set; } = new();
}

/// <summary>
/// Windows 启动项全维扫描与无损治理医生 (StartupDoctor)：
/// 整合 SystemTools\扫启动项.ps1 的 8 大维度：
/// 1. 注册表 Run/RunOnce (HKLM/HKCU/WOW6432Node) + StartupApproved 状态同步；
/// 2. 用户与公共启动文件夹；
/// 3. 开机自动启动的第三方服务 (Start=Auto)；
/// 4. 非微软计划任务；
/// 5. Winsock NSP 底层注入；
/// 6. AppInit_DLLs 界面注入；
/// 7. Explorer 图标叠加角标 (ShellIconOverlayIdentifiers > 15 拖慢提示)；
/// 8. 核心护栏白名单，支持无损禁用与一键还原。
/// </summary>
public static class StartupDoctor
{
    // 绝对不可禁用的系统/硬件/代理关键词
    private static readonly string[] ProtectedKeywords =
    {
        "windows", "defender", "securityhealth", "ctfmon", "explorer", "dwm",
        "nvidia", "nvcontainer", "amd", "radeon", "realtek", "intel", "audio",
        "flclash", "clash", "v2ray", "mihomo", "pawnio", "douzhanzhe", "lpcacpiec",
        "bthserv", "wlan", "dhcp", "dnscache", "rpc", "winmgmt", "task", "ime"
    };

    // 常见可安全禁用/延后的冗余更新或推广项
    private static readonly string[] OptimizableKeywords =
    {
        "updater", "update", "helper", "telemetry", "crash", "assistant",
        "adobe", "wps", "baidunetdisk", "xunlei", "thunder", "qqbrowser",
        "sogou", "youdao", "dingtalk", "feishu", "gameviewer", "crossdesk",
        "steam", "epic", "onedrive", "edgeupdate", "googleupdate"
    };

    public static StartupReport Scan()
    {
        var report = new StartupReport();

        try
        {
            ScanRegistryRun(report);
            ScanStartupFolders(report);
            ScanAutoServices(report);
            ScanScheduledTasks(report);
            ScanWinsockNsp(report);
            ScanAppInitDlls(report);
            ScanShellOverlays(report);

            report.SummaryMessage =
                $"共扫出 {report.TotalItems} 项开机与驻留项：其中常规自启 {report.RegistryAndFolderCount} 项、第三方服务 {report.AutoServiceCount} 项、计划任务 {report.ScheduledTaskCount} 项；" +
                $"底层注入 (NSP={report.ThirdPartyNspCount}, AppInit={report.AppInitCount})，Explorer 角标 {report.ShellOverlayCount} 个" +
                (report.IsOverlayBloated ? "（⚠️ 超过 15 个会拖慢界面）" : "") +
                $"。建议可优化项：{report.OptimizableCount} 个。";
        }
        catch (Exception ex)
        {
            report.SummaryMessage = $"启动项扫描遇到部分异常: {ex.Message}";
        }

        return report;
    }

    private static (string Risk, string Reason) Classify(string name, string command, string category)
    {
        var text = $"{name} {command}".ToLowerInvariant();

        if (ProtectedKeywords.Any(k => text.Contains(k)))
        {
            return ("Protected", "系统核心、硬件驱动或网络代理守护项（受保护禁止禁用）");
        }

        if (category == "WinsockNsp" || category == "AppInitDll" || category == "ShellOverlay")
        {
            return ("Caution", "底层挂钩/外壳注入项，仅在排查卡顿或网络异常时处理");
        }

        if (OptimizableKeywords.Any(k => text.Contains(k)))
        {
            return ("SafeToDisable", "后台驻留/自动更新/非必要开机启动项，可安全禁用以提速开机与降低后台占用");
        }

        return ("Caution", "第三方应用自启项，可按日常使用频率决定是否保留");
    }

    private static bool IsApprovedEnabled(RegistryHive hive, string approvedSubKey, string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{approvedSubKey}");
            if (key?.GetValue(valueName) is byte[] bytes && bytes.Length >= 1)
            {
                // 0x02 或 0x06 为启用，0x03 为禁用
                return bytes[0] == 0x02 || bytes[0] == 0x06;
            }
        }
        catch { }
        return true;
    }

    private static void ScanRegistryRun(StartupReport report)
    {
        var targets = new[]
        {
            (Hive: RegistryHive.LocalMachine, View: RegistryView.Registry64, Path: @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", Approved: "Run", Label: "HKLM\\...\\Run"),
            (Hive: RegistryHive.LocalMachine, View: RegistryView.Registry32, Path: @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", Approved: "Run32", Label: "HKLM(32)\\...\\Run"),
            (Hive: RegistryHive.CurrentUser,  View: RegistryView.Registry64, Path: @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", Approved: "Run", Label: "HKCU\\...\\Run"),
        };

        foreach (var t in targets)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(t.Hive, t.View);
                using var key = baseKey.OpenSubKey(t.Path);
                if (key == null) continue;

                foreach (var valName in key.GetValueNames())
                {
                    if (string.IsNullOrWhiteSpace(valName)) continue;
                    var cmd = key.GetValue(valName)?.ToString() ?? "";
                    var enabled = IsApprovedEnabled(t.Hive, t.Approved, valName);
                    var (risk, reason) = Classify(valName, cmd, "RegistryRun");

                    report.Items.Add(new StartupItem
                    {
                        Id = $"{t.Label}:{valName}",
                        Name = valName,
                        Category = "RegistryRun",
                        Location = t.Label,
                        Command = cmd,
                        RawKeyPath = t.Path,
                        ApprovedSubKey = t.Approved,
                        IsHklm = t.Hive == RegistryHive.LocalMachine,
                        IsEnabled = enabled,
                        RiskLevel = risk,
                        Reason = reason
                    });
                    report.RegistryAndFolderCount++;
                }
            }
            catch { }
        }
    }

    private static void ScanStartupFolders(StartupReport report)
    {
        var folders = new[]
        {
            (Path: Environment.GetFolderPath(Environment.SpecialFolder.Startup), Hive: RegistryHive.CurrentUser, Label: "用户启动文件夹"),
            (Path: Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Hive: RegistryHive.LocalMachine, Label: "公共启动文件夹"),
        };

        foreach (var f in folders)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(f.Path) || !Directory.Exists(f.Path)) continue;
                foreach (var file in Directory.GetFiles(f.Path))
                {
                    var name = Path.GetFileName(file);
                    if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                    var enabled = IsApprovedEnabled(f.Hive, "StartupFolder", name);
                    var (risk, reason) = Classify(name, file, "StartupFolder");

                    report.Items.Add(new StartupItem
                    {
                        Id = $"Folder:{file}",
                        Name = name,
                        Category = "StartupFolder",
                        Location = f.Label,
                        Command = file,
                        ApprovedSubKey = "StartupFolder",
                        IsHklm = f.Hive == RegistryHive.LocalMachine,
                        IsEnabled = enabled,
                        RiskLevel = risk,
                        Reason = reason
                    });
                    report.RegistryAndFolderCount++;
                }
            }
            catch { }
        }
    }

    private static void ScanAutoServices(StartupReport report)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DisplayName, PathName, StartMode, State FROM Win32_Service WHERE StartMode = 'Auto'");
            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    var name = obj["Name"]?.ToString() ?? "";
                    var display = obj["DisplayName"]?.ToString() ?? name;
                    var path = obj["PathName"]?.ToString() ?? "";
                    var state = obj["State"]?.ToString() ?? "";

                    // 跳过 Windows 系统自带核心服务目录
                    bool isSystemPath = path.IndexOf(@"\Windows\system32\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        path.IndexOf(@"\Windows\SysWOW64\", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                        path.IndexOf(@"\Windows\servicing\", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isSystemPath) continue;

                    var (risk, reason) = Classify(display, $"{name} {path}", "AutoService");
                    report.Items.Add(new StartupItem
                    {
                        Id = $"Service:{name}",
                        Name = $"{display} ({name})",
                        Category = "AutoService",
                        Location = $"状态: {state}",
                        Command = path,
                        RawKeyPath = name,
                        IsEnabled = true,
                        RiskLevel = risk,
                        Reason = reason
                    });
                    report.AutoServiceCount++;
                }
            }
        }
        catch { }
    }

    private static void ScanScheduledTasks(StartupReport report)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = "/query /fo CSV /nh /v",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return;
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(5000);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var reader = new StringReader(output);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var cols = ParseCsvLine(line);
                if (cols.Count < 9) continue;

                var taskName = cols[1];
                var status = cols[3];
                var taskToRun = cols[8];

                if (taskName.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)) continue;
                if (!seen.Add(taskName)) continue;

                bool isEnabled = !status.Equals("Disabled", StringComparison.OrdinalIgnoreCase) && !status.Contains("禁用");
                var (risk, reason) = Classify(taskName, taskToRun, "ScheduledTask");

                report.Items.Add(new StartupItem
                {
                    Id = $"Task:{taskName}",
                    Name = taskName.TrimStart('\\'),
                    Category = "ScheduledTask",
                    Location = $"任务状态: {status}",
                    Command = taskToRun,
                    RawKeyPath = taskName,
                    IsEnabled = isEnabled,
                    RiskLevel = risk,
                    Reason = reason
                });
                report.ScheduledTaskCount++;
            }
        }
        catch { }
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result;
    }

    private static void ScanWinsockNsp(StartupReport report)
    {
        foreach (var sub in new[] { "Catalog_Entries64", "Catalog_Entries" })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var cat = baseKey.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\Winsock2\Parameters\Namespace_Catalog5\{sub}");
                if (cat == null) continue;

                foreach (var entryName in cat.GetSubKeyNames())
                {
                    using var entry = cat.OpenSubKey(entryName);
                    var lib = entry?.GetValue("LibraryPath")?.ToString() ?? "";
                    var disp = entry?.GetValue("DisplayString")?.ToString() ?? entryName;
                    if (string.IsNullOrWhiteSpace(lib) || lib.IndexOf("SystemRoot", StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    report.ThirdPartyNspCount++;
                    report.Items.Add(new StartupItem
                    {
                        Id = $"NSP:{sub}:{entryName}",
                        Name = disp,
                        Category = "WinsockNsp",
                        Location = sub,
                        Command = lib,
                        IsEnabled = true,
                        RiskLevel = "Caution",
                        Reason = "第三方 Winsock 命名空间提供程序（会被加载进所有联网进程）"
                    });
                }
            }
            catch { }
        }
    }

    private static void ScanAppInitDlls(StartupReport report)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows");
                if (key == null) continue;

                var dlls = key.GetValue("AppInit_DLLs")?.ToString()?.Trim() ?? "";
                var load = Convert.ToInt32(key.GetValue("LoadAppInit_DLLs") ?? 0);
                if (!string.IsNullOrEmpty(dlls))
                {
                    report.AppInitCount++;
                    report.Items.Add(new StartupItem
                    {
                        Id = $"AppInit:{view}",
                        Name = $"AppInit_DLLs ({view})",
                        Category = "AppInitDll",
                        Location = $"LoadAppInit_DLLs={load}",
                        Command = dlls,
                        IsEnabled = load != 0,
                        RiskLevel = "Caution",
                        Reason = "会被强制注入所有图形界面进程，若非安全软件需要建议清空"
                    });
                }
            }
            catch { }
        }
    }

    private static void ScanShellOverlays(StartupReport report)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers");
            if (key == null) return;

            var names = key.GetSubKeyNames();
            report.ShellOverlayCount = names.Length;

            foreach (var n in names)
            {
                report.Items.Add(new StartupItem
                {
                    Id = $"Overlay:{n}",
                    Name = n.Trim(),
                    Category = "ShellOverlay",
                    Location = "ShellIconOverlayIdentifiers",
                    Command = n,
                    IsEnabled = true,
                    RiskLevel = names.Length > 15 ? "Caution" : "Protected",
                    Reason = names.Length > 15
                        ? $"当前角标总数 {names.Length} 个（超过 Windows 15 个限制，会拖慢 Explorer 刷新）"
                        : "Explorer 图标叠加角标处理器"
                });
            }
        }
        catch { }
    }

    /// <summary>
    /// 安全无损禁用或启用启动项（Protected 项拒绝操作）。
    /// </summary>
    public static (bool Succeeded, string Message) ToggleItem(StartupItem item, bool enable)
    {
        if (item.RiskLevel == "Protected")
        {
            return (false, $"[{item.Name}] 属于系统核心、驱动或代理保护项，已受安全护栏锁定，禁止修改。");
        }

        try
        {
            if (item.Category == "RegistryRun" || item.Category == "StartupFolder")
            {
                var hive = item.IsHklm ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
                using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var appKey = baseKey.CreateSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\{item.ApprovedSubKey}");
                if (appKey == null) return (false, "无法打开 StartupApproved 注册表键（可能需要管理员权限）。");

                byte[] flag = enable
                    ? new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }
                    : new byte[] { 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

                appKey.SetValue(item.Name, flag, RegistryValueKind.Binary);
                item.IsEnabled = enable;
                return (true, $"已{(enable ? "恢复启用" : "无损禁用")}启动项 [{item.Name}]（保留原键值，随时可恢复）。");
            }

            if (item.Category == "AutoService")
            {
                var mode = enable ? "auto" : "demand";
                var (code, outStr) = RunHiddenTool("sc.exe", $"config \"{item.RawKeyPath}\" start= {mode}");
                if (code == 0)
                {
                    item.IsEnabled = enable;
                    return (true, $"已将服务 [{item.RawKeyPath}] 启动类型设为 {(enable ? "自动 (auto)" : "手动 (demand)")}。");
                }
                return (false, $"修改服务失败 (需要管理员权限): {outStr}");
            }

            if (item.Category == "ScheduledTask")
            {
                var flag = enable ? "/ENABLE" : "/DISABLE";
                var (code, outStr) = RunHiddenTool("schtasks.exe", $"/Change /TN \"{item.RawKeyPath}\" {flag}");
                if (code == 0)
                {
                    item.IsEnabled = enable;
                    return (true, $"已{(enable ? "启用" : "禁用")}计划任务 [{item.Name}]。");
                }
                return (false, $"修改计划任务失败 (需要管理员权限): {outStr}");
            }

            return (false, "该类型条目暂不支持直接切换状态。");
        }
        catch (Exception ex)
        {
            return (false, $"操作失败: {ex.Message}");
        }
    }

    private static (int ExitCode, string Output) RunHiddenTool(string fileName, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        using var p = Process.Start(psi);
        if (p == null) return (-1, "无法启动进程");
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(6000);
        return (p.ExitCode, $"{stdout} {stderr}".Trim());
    }
}
