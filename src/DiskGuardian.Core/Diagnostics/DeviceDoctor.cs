using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Management;
using DiskGuardian.Core.Engine;

namespace DiskGuardian.Core.Diagnostics;

public sealed class PnpErrorDeviceItem
{
    public string Name { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public int ErrorCode { get; set; }
    public string PnpClass { get; set; } = string.Empty;
}

public sealed class InputHostStatusItem
{
    public string ProcessName { get; set; } = string.Empty;
    public int Pid { get; set; }
    public long WorkingSetBytes { get; set; }
    public string WorkingSetHuman => CleanerEngine.HumanSize(WorkingSetBytes);
    public int ThreadCount { get; set; }
    public bool IsBloated =>
        (ProcessName.Equals("TextInputHost", StringComparison.OrdinalIgnoreCase) && (WorkingSetBytes > 120L * 1024 * 1024 || ThreadCount > 60)) ||
        (ProcessName.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase) && WorkingSetBytes > 140L * 1024 * 1024) ||
        (ProcessName.Equals("SearchHost", StringComparison.OrdinalIgnoreCase) && WorkingSetBytes > 140L * 1024 * 1024);
}

public sealed class DeviceDiagnosticReport
{
    public int BthStormCount { get; set; }
    public int UsbResetCount { get; set; }
    public int TotalStormEvents => BthStormCount + UsbResetCount;
    public DateTime? LastStormTime { get; set; }
    public string LastStormSummary { get; set; } = string.Empty;

    public string BluetoothAdapterName { get; set; } = "未检测到";
    public string BluetoothAdapterId { get; set; } = string.Empty;
    public bool BluetoothAdapterHealthy { get; set; } = true;

    public List<PnpErrorDeviceItem> ErrorDevices { get; set; } = new();
    public List<InputHostStatusItem> InputHosts { get; set; } = new();
    public bool HasBloatedInputHost => InputHosts.Any(h => h.IsBloated);

    public string HealthRating { get; set; } = "healthy"; // healthy, warning, storm
    public string HealthRatingDisplay => HealthRating switch
    {
        "storm" => "🔴 硬件重连风暴",
        "warning" => "⚠️ 输入/设备异常",
        _ => "✅ 硬件与输入稳定"
    };

    public string SummaryMessage { get; set; } = string.Empty;
    public List<string> Details { get; set; } = new();
}

/// <summary>
/// 硬件掉线与 PnP 重连风暴诊断医生：
/// 定位蓝牙/USB 掉驱动触发的系统级 PnP 枚举风暴，以及 Win 键/输入法宿主进程卡死膨胀问题。
/// </summary>
public static class DeviceDoctor
{
    private static readonly string[] InputHostNames =
    {
        "StartMenuExperienceHost",
        "SearchHost",
        "TextInputHost"
    };

    public static DeviceDiagnosticReport Diagnose()
    {
        var report = new DeviceDiagnosticReport();

        try
        {
            ScanSystemStormEvents(report);
            ScanPnpDevices(report);
            ScanInputHosts(report);

            if (report.TotalStormEvents >= 3)
            {
                report.HealthRating = "storm";
                report.SummaryMessage =
                    $"近 15 分钟检测到 {report.TotalStormEvents} 次硬件掉线重连风暴" +
                    (report.BthStormCount > 0 ? $"（BTHUSB 蓝牙报错 {report.BthStormCount} 次）" : "") +
                    "，正在阻塞 Win 键与桌面输入！建议先点击【🔄 刷新输入与开始菜单】或临时禁用异常蓝牙，再彻底断电冷重启。";
            }
            else if (report.ErrorDevices.Count > 0 || report.HasBloatedInputHost)
            {
                report.HealthRating = "warning";
                var parts = new List<string>();
                if (report.ErrorDevices.Count > 0) parts.Add($"{report.ErrorDevices.Count} 个 PnP 设备存在错误码");
                if (report.HasBloatedInputHost) parts.Add("开始菜单/输入法宿主进程内存或线程膨胀");
                report.SummaryMessage = $"检测到 {string.Join("，", parts)}，可能导致 Win 键或回车卡顿。";
            }
            else
            {
                report.HealthRating = "healthy";
                report.SummaryMessage = "近 15 分钟未见 BTHUSB / USB 重连风暴，蓝牙适配器与桌面输入宿主进程状态均稳定。";
            }
        }
        catch (Exception ex)
        {
            report.HealthRating = "warning";
            report.SummaryMessage = $"硬件与输入诊断遇到部分异常: {ex.Message}";
            report.Details.Add(ex.ToString());
        }

        return report;
    }

    private static void ScanSystemStormEvents(DeviceDiagnosticReport report)
    {
        try
        {
            // 查询最近 15 分钟 System 日志中 BTHUSB (16,17,18) 或 USBHUB3/USBXHCI 错误/警告
            const string queryStr =
                "*[System[(" +
                "(Provider[@Name='BTHUSB'] and (EventID=16 or EventID=17 or EventID=18)) or " +
                "(Provider[@Name='Microsoft-Windows-USB-USBHUB3' or @Name='USBXHCI'] and (Level=2 or Level=3))" +
                ") and TimeCreated[timediff(@SystemTime) <= 900000]]]";

            var query = new EventLogQuery("System", PathType.LogName, queryStr)
            {
                ReverseDirection = true
            };

            using var reader = new EventLogReader(query);
            EventRecord? rec;
            int inspected = 0;
            while ((rec = reader.ReadEvent()) != null && inspected < 100)
            {
                using (rec)
                {
                    inspected++;
                    var provider = rec.ProviderName ?? "";
                    if (provider.Equals("BTHUSB", StringComparison.OrdinalIgnoreCase))
                    {
                        report.BthStormCount++;
                    }
                    else
                    {
                        report.UsbResetCount++;
                    }

                    if (report.LastStormTime == null)
                    {
                        report.LastStormTime = rec.TimeCreated;
                        try
                        {
                            var desc = rec.FormatDescription();
                            report.LastStormSummary = string.IsNullOrWhiteSpace(desc)
                                ? $"{provider} EventID={rec.Id}"
                                : desc.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                        }
                        catch
                        {
                            report.LastStormSummary = $"{provider} EventID={rec.Id}";
                        }
                    }

                    if (report.Details.Count < 8)
                    {
                        report.Details.Add($"[{rec.TimeCreated:HH:mm:ss}] {provider} (ID={rec.Id})");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            report.Details.Add($"[EventLog] 读取系统日志失败: {ex.Message}");
        }
    }

    private static void ScanPnpDevices(DeviceDiagnosticReport report)
    {
        try
        {
            using var bthSearcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID, ConfigManagerErrorCode, Status FROM Win32_PnPEntity WHERE PNPClass = 'Bluetooth' AND DeviceID LIKE 'USB\\\\%'");
            foreach (ManagementObject obj in bthSearcher.Get())
            {
                using (obj)
                {
                    var name = obj["Name"]?.ToString() ?? "Bluetooth USB Adapter";
                    var devId = obj["DeviceID"]?.ToString() ?? "";
                    var err = Convert.ToInt32(obj["ConfigManagerErrorCode"] ?? 0);
                    report.BluetoothAdapterName = name;
                    report.BluetoothAdapterId = devId;
                    report.BluetoothAdapterHealthy = err == 0;
                    break;
                }
            }

            using var errSearcher = new ManagementObjectSearcher(
                "SELECT Name, DeviceID, ConfigManagerErrorCode, PNPClass, Status FROM Win32_PnPEntity WHERE ConfigManagerErrorCode != 0");
            foreach (ManagementObject obj in errSearcher.Get())
            {
                using (obj)
                {
                    var err = Convert.ToInt32(obj["ConfigManagerErrorCode"] ?? 0);
                    // 跳过已拔出/休眠的纯幽灵设备 (45 = CM_PROB_PHANTOM)
                    if (err == 45) continue;

                    var item = new PnpErrorDeviceItem
                    {
                        Name = obj["Name"]?.ToString() ?? "未知设备",
                        DeviceId = obj["DeviceID"]?.ToString() ?? "",
                        ErrorCode = err,
                        PnpClass = obj["PNPClass"]?.ToString() ?? ""
                    };
                    report.ErrorDevices.Add(item);
                    if (report.Details.Count < 15)
                    {
                        report.Details.Add($"[PnP Error {err}] {item.Name} ({item.DeviceId})");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            report.Details.Add($"[PnP] 设备枚举失败: {ex.Message}");
        }
    }

    private static void ScanInputHosts(DeviceDiagnosticReport report)
    {
        foreach (var procName in InputHostNames)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(procName))
                {
                    using (p)
                    {
                        report.InputHosts.Add(new InputHostStatusItem
                        {
                            ProcessName = procName,
                            Pid = p.Id,
                            WorkingSetBytes = p.WorkingSet64,
                            ThreadCount = p.Threads.Count
                        });
                    }
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// 安全重启卡死的开始菜单、搜索和输入法宿主进程（绝不触碰 explorer.exe），1 秒恢复 Win 键与回车响应。
    /// </summary>
    public static (bool Succeeded, int ResetCount, string Message) ResetInputHosts()
    {
        int killed = 0;
        var names = new List<string>();
        foreach (var procName in InputHostNames)
        {
            try
            {
                var procs = Process.GetProcessesByName(procName);
                foreach (var p in procs)
                {
                    using (p)
                    {
                        try
                        {
                            p.Kill();
                            killed++;
                            if (!names.Contains(procName)) names.Add(procName);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        if (killed > 0)
        {
            return (true, killed,
                $"已安全刷新 {killed} 个桌面输入与菜单宿主进程 ({string.Join(", ", names)})，Windows 将在 1 秒内自动拉起干净实例（未触碰 explorer.exe，桌面与窗口零丢失）。");
        }

        return (true, 0, "当前未发现活跃的卡死输入宿主进程，或系统已处于干净状态。");
    }

    /// <summary>
    /// 使用 pnputil 临时禁用引发重连风暴的 USB 蓝牙适配器（零黑框，需管理员权限），止住 PnP 枚举卡死。
    /// </summary>
    public static (bool Succeeded, string Message) DisableStormBluetooth(string? instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return (false, "未找到可禁用的 USB 蓝牙适配器实例 ID。");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = $"/disable-device \"{instanceId}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return (false, "无法启动 pnputil.exe");

            string stdout = proc.StandardOutput.ReadToEnd();
            string stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(8000);

            if (proc.ExitCode == 0)
            {
                return (true,
                    $"已临时禁用异常蓝牙适配器 ({instanceId})，BTHUSB 重连风暴已止住！\n\n后续如需恢复蓝牙，请先保存工作并彻底重启/断电冷启动电脑。");
            }

            return (false, $"禁用失败 (ExitCode={proc.ExitCode})。请确认已以管理员权限运行 DiskGuardian。\n{stderr} {stdout}".Trim());
        }
        catch (Exception ex)
        {
            return (false, $"执行 pnputil 失败: {ex.Message}");
        }
    }
}
