using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DiskGuardian.Core.Diagnostics;

public sealed class PowerDiagnosticReport
{
    public string ActiveSchemeGuid { get; set; } = string.Empty;
    public string ActiveSchemeName { get; set; } = "未知方案";

    public int? PerfEppAc { get; set; }
    public int? PerfEppDc { get; set; }
    public int? PerfEpp1Ac { get; set; }
    public int? PerfEpp1Dc { get; set; }
    public int? CpMinCoresAc { get; set; }
    public int? PerfBoostModeAc { get; set; }

    public bool DouzhanzheRunning { get; set; }

    public string HealthRating { get; set; } = "optimal"; // optimal, aggressive, warning
    public string HealthRatingDisplay => HealthRating switch
    {
        "aggressive" => "⚠️ 轻载偏激进",
        "warning" => "⚠️ 检测受限",
        _ => "✅ 调度均衡"
    };

    public string EppDisplay => PerfEppAc.HasValue ? $"{PerfEppAc.Value}% (DC {PerfEppDc ?? 0}%)" : "--";
    public string Epp1Display => PerfEpp1Ac.HasValue ? $"{PerfEpp1Ac.Value}% (DC {PerfEpp1Dc ?? 0}%)" : "--";
    public string CoreParkDisplay => CpMinCoresAc.HasValue
        ? (CpMinCoresAc.Value >= 95 ? $"{CpMinCoresAc.Value}% (不休止)" : $"{CpMinCoresAc.Value}% (允许休止)")
        : "--";

    public string BoostModeDisplay => PerfBoostModeAc switch
    {
        0 => "0 - 已禁用",
        1 => "1 - 已启用",
        2 => "2 - 高性能",
        3 => "3 - 高效率",
        4 => "4 - 高性能高效率",
        5 => "5 - 积极且有保障",
        6 => "6 - 高效积极",
        _ => PerfBoostModeAc?.ToString() ?? "--"
    };

    public string SummaryMessage { get; set; } = string.Empty;
    public List<string> Details { get; set; } = new();
}

/// <summary>
/// Windows 电源调度与轻载温控偏好诊断医生 (PowerDoctor)：
/// 检查电源方案的能源性能偏好 (PERFEPP / PERFEPP1)、核心休止下限 (CPMINCORES) 与睿频策略 (PERFBOOSTMODE)，
/// 并提供免管理员权限的温和降温调优（仅调整 PERFEPP 45% / PERFEPP1 50%，不牺牲满载性能）。
/// </summary>
public static class PowerDoctor
{
    private const string SubProcessor = "54533251-82be-4824-96c1-47b60b740d00";
    private const string GuidPerfEpp = "36687f9e-e3a5-4dbf-b1dc-15eb381c6863";
    private const string GuidPerfEpp1 = "36687f9e-e3a5-4dbf-b1dc-15eb381c6864";
    private const string GuidCpMinCores = "0cc5b647-c1df-4637-891a-dec35c318583";
    private const string GuidPerfBoost = "be337238-0d82-4146-a960-4f3749d470c7";

    private static readonly Regex SchemeGuidRegex = new(
        @"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SchemeNameRegex = new(
        @"\(([^()]+)\)\s*$",
        RegexOptions.Compiled);

    private static readonly Regex HexValueRegex = new(
        @"0x([0-9a-fA-F]+)",
        RegexOptions.Compiled);

    public static PowerDiagnosticReport Diagnose()
    {
        var report = new PowerDiagnosticReport();

        try
        {
            var (guid, name) = GetActiveScheme();
            report.ActiveSchemeGuid = guid;
            report.ActiveSchemeName = name;

            if (string.IsNullOrWhiteSpace(guid))
            {
                report.HealthRating = "warning";
                report.SummaryMessage = "无法读取当前活动 Windows 电源方案。";
                return report;
            }

            var (eppAc, eppDc) = QueryAcDcValue(guid, GuidPerfEpp);
            var (epp1Ac, epp1Dc) = QueryAcDcValue(guid, GuidPerfEpp1);
            var (cpMinAc, _) = QueryAcDcValue(guid, GuidCpMinCores);
            var (boostAc, _) = QueryAcDcValue(guid, GuidPerfBoost);

            report.PerfEppAc = eppAc;
            report.PerfEppDc = eppDc;
            report.PerfEpp1Ac = epp1Ac;
            report.PerfEpp1Dc = epp1Dc;
            report.CpMinCoresAc = cpMinAc;
            report.PerfBoostModeAc = boostAc;

            try
            {
                var apiProcs = Process.GetProcessesByName("Douzhanzhe.API");
                report.DouzhanzheRunning = apiProcs.Length > 0;
                foreach (var p in apiProcs) p.Dispose();
            }
            catch { }

            bool eppAggressive = eppAc.HasValue && eppAc.Value <= 15;
            bool coreParkLocked = cpMinAc.HasValue && cpMinAc.Value >= 95;

            if (eppAggressive || coreParkLocked)
            {
                report.HealthRating = "aggressive";
                var reasons = new List<string>();
                if (eppAggressive)
                {
                    reasons.Add($"交流性能核偏好 PERFEPP={eppAc}% 偏低（易在轻载时顶高频高电压）");
                }
                if (coreParkLocked)
                {
                    reasons.Add($"最小核心不休止 CPMINCORES={cpMinAc}%（全核常驻唤醒）");
                }
                report.SummaryMessage = $"当前电源方案 [{name}] 调度偏激进：{string.Join("；", reasons)}。";
            }
            else
            {
                report.HealthRating = "optimal";
                report.SummaryMessage = $"当前电源方案 [{name}] 调度均衡（PERFEPP={report.EppDisplay}，PERFEPP1={report.Epp1Display}）。";
            }

            report.Details.Add($"活动方案: {name} ({guid})");
            report.Details.Add($"PERFEPP (性能核偏好): AC={eppAc?.ToString() ?? "?"}% / DC={eppDc?.ToString() ?? "?"}%");
            report.Details.Add($"PERFEPP1 (能效核偏好): AC={epp1Ac?.ToString() ?? "?"}% / DC={epp1Dc?.ToString() ?? "?"}%");
            report.Details.Add($"CPMINCORES (最小不休止核): AC={cpMinAc?.ToString() ?? "?"}% | PERFBOOSTMODE: {report.BoostModeDisplay}");
            if (coreParkLocked && report.DouzhanzheRunning)
            {
                report.Details.Add("提示: 检测到 Douzhanzhe.API 正在运行。若旧版配置开启 turboEnabled=true，ParameterGuard 会周期性写回核心不休止策略，建议同时配合开启自定义风扇曲线或切换安静档。");
            }
        }
        catch (Exception ex)
        {
            report.HealthRating = "warning";
            report.SummaryMessage = $"电源诊断异常: {ex.Message}";
            report.Details.Add(ex.ToString());
        }

        return report;
    }

    /// <summary>
    /// 免管理员权限温和降温调优：将当前活动方案插电 PERFEPP 设为 45%、PERFEPP1 设为 50%，
    /// 允许轻载时快速降频降温，同时不限制满载与游戏性能。
    /// </summary>
    public static (bool Succeeded, string Message) OptimizeGentle(int acEpp = 45, int acEpp1 = 50)
    {
        try
        {
            var (guid, name) = GetActiveScheme();
            if (string.IsNullOrWhiteSpace(guid))
            {
                return (false, "无法获取当前电源方案 GUID。");
            }

            int clampedEpp = Math.Clamp(acEpp, 0, 100);
            int clampedEpp1 = Math.Clamp(acEpp1, 0, 100);

            var (ok1, err1) = RunPowerCfg($"/setacvalueindex {guid} {SubProcessor} {GuidPerfEpp} {clampedEpp}");
            var (ok2, err2) = RunPowerCfg($"/setacvalueindex {guid} {SubProcessor} {GuidPerfEpp1} {clampedEpp1}");
            var (ok3, err3) = RunPowerCfg($"/setactive {guid}");

            if (ok1 && ok2 && ok3)
            {
                return (true, $"已对方案 [{name}] 应用温和降温偏好（PERFEPP={clampedEpp}%, PERFEPP1={clampedEpp1}%），并立即刷新生效。");
            }

            return (false, $"部分 powercfg 指令未成功: {err1} {err2} {err3}".Trim());
        }
        catch (Exception ex)
        {
            return (false, $"电源调优失败: {ex.Message}");
        }
    }

    public static (bool Succeeded, string Message) RestoreDefault(int acEpp = 10, int acEpp1 = 33)
    {
        return OptimizeGentle(acEpp, acEpp1);
    }

    private static (string Guid, string Name) GetActiveScheme()
    {
        var (ok, output) = RunPowerCfg("/getactivescheme");
        if (!ok || string.IsNullOrWhiteSpace(output)) return (string.Empty, string.Empty);

        var guidMatch = SchemeGuidRegex.Match(output);
        if (!guidMatch.Success) return (string.Empty, string.Empty);

        var nameMatch = SchemeNameRegex.Match(output.Trim());
        var name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : "当前电源方案";
        return (guidMatch.Groups[1].Value, name);
    }

    private static (int? Ac, int? Dc) QueryAcDcValue(string schemeGuid, string settingGuid)
    {
        var (ok, output) = RunPowerCfg($"/query {schemeGuid} {SubProcessor} {settingGuid}");
        if (!ok || string.IsNullOrWhiteSpace(output)) return (null, null);

        var matches = HexValueRegex.Matches(output);
        if (matches.Count >= 2 &&
            int.TryParse(matches[^2].Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int ac) &&
            int.TryParse(matches[^1].Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int dc))
        {
            return (ac, dc);
        }

        if (matches.Count == 1 &&
            int.TryParse(matches[0].Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int single))
        {
            return (single, single);
        }

        return (null, null);
    }

    private static (bool Ok, string Output) RunPowerCfg(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powercfg.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var proc = Process.Start(psi);
        if (proc == null) return (false, "无法启动 powercfg.exe");

        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(4000);
        return (proc.ExitCode == 0, $"{stdout} {stderr}".Trim());
    }
}
