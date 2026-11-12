using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

namespace DiskGuardian.Core.Diagnostics;

/// <summary>
/// 资源管理器与系统外壳诊断报告
/// </summary>
public sealed record ExplorerDiagnosisResult(
    bool HasAnomaly,
    bool IsWin11,
    bool IsModernShellBlocked,
    string BlockedLocations,
    bool SeparateProcessEnabled,
    string ShellArchitecture,
    string Summary,
    List<string> Details,
    string BlockedStatusDisplay,
    string SeparateProcessDisplay,
    string SystemVersionDisplay,
    string IntegrityStatusDisplay);

/// <summary>
/// 资源管理器与系统外壳修复报告
/// </summary>
public sealed record ExplorerRepairResult(
    bool Succeeded,
    List<string> ActionsTaken,
    string? ErrorMessage,
    bool RequiresRestart);

/// <summary>
/// Windows 资源管理器与桌面外壳健康医生 (ExplorerDoctor)：
/// 1. 深度检测 Win11 现代 Fluent 命令栏与多标签页是否被注册表阻断 (CLSID {e2bf9676-5f8f-435c-97eb-11607a5bedf7})；
/// 2. 探测导致多标签页宿主脱轨的独立进程设置 (SeparateProcess)；
/// 3. 提供一键安全还原 Win11 现代原生外壳能力；
/// 4. 遵守 Windows 桌面安全铁律：通过 Win32 API 广播外壳变更，严禁暴力杀死 explorer.exe，杜绝桌面微件坍塌。
/// </summary>
public static class ExplorerDoctor
{
    private const string Win11ModernCommandBarClsid = "{e2bf9676-5f8f-435c-97eb-11607a5bedf7}";
    private const string ShellExtensionsBlockedKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Blocked";
    private const string ExplorerAdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    // Win32 API：外壳设置变更通知广播
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        string lParam,
        uint fuFlags,
        uint uTimeout,
        out UIntPtr lpdwResult);

    private static readonly IntPtr HWND_BROADCAST = new(0xffff);
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    /// <summary>
    /// 全面体检系统外壳与资源管理器配置状态
    /// </summary>
    public static ExplorerDiagnosisResult Diagnose()
    {
        var details = new List<string>();
        bool isWin11 = Environment.OSVersion.Version.Build >= 22000;
        int buildNumber = Environment.OSVersion.Version.Build;
        string sysVersionDisplay = isWin11 ? $"Win11 (Build {buildNumber})" : $"Win10/其他 (Build {buildNumber})";

        bool hkcuBlocked = false;
        bool hklmBlocked = false;
        bool separateProcess = false;

        // 1. 探测注册表 HKCU / HKLM 是否阻断现代命令栏
        try
        {
            using var hkcu = Registry.CurrentUser.OpenSubKey(ShellExtensionsBlockedKey, false);
            if (hkcu != null && hkcu.GetValue(Win11ModernCommandBarClsid) != null)
            {
                hkcuBlocked = true;
            }
        }
        catch (Exception ex)
        {
            details.Add($"[排查异常] 读取 HKCU Blocked 注册表失败: {ex.Message}");
        }

        try
        {
            using var hklm = Registry.LocalMachine.OpenSubKey(ShellExtensionsBlockedKey, false);
            if (hklm != null && hklm.GetValue(Win11ModernCommandBarClsid) != null)
            {
                hklmBlocked = true;
            }
        }
        catch (Exception ex)
        {
            details.Add($"[排查异常] 读取 HKLM Blocked 注册表失败: {ex.Message}");
        }

        // 2. 探测是否启用了独立进程模式
        try
        {
            using var adv = Registry.CurrentUser.OpenSubKey(ExplorerAdvancedKey, false);
            if (adv != null)
            {
                var val = adv.GetValue("SeparateProcess", 0);
                separateProcess = Convert.ToInt32(val) != 0;
            }
        }
        catch (Exception ex)
        {
            details.Add($"[排查异常] 读取 SeparateProcess 设置失败: {ex.Message}");
        }

        // 3. 探测当前运行上下文权限
        bool isElevated = false;
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(id);
            isElevated = principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { }

        string integrityDisplay = isElevated ? "管理员提权模式 (可能触发Win32回退)" : "标准用户权限 (正常)";

        // 汇总指标
        bool modernBlocked = hkcuBlocked || hklmBlocked;
        string blockedLocations = (hkcuBlocked, hklmBlocked) switch
        {
            (true, true) => "HKCU + HKLM 同时阻断",
            (true, false) => "HKCU 用户级阻断",
            (false, true) => "HKLM 系统级阻断",
            _ => "未阻断 (正常)"
        };

        string blockedDisplay = modernBlocked ? $"异常拦截 ({blockedLocations})" : "正常放行 (Fluent 原生)";
        string sepDisplay = separateProcess ? "已开启 (会破坏多标签页)" : "未开启 (单宿主共享)";

        bool hasAnomaly = modernBlocked || separateProcess;

        string shellArch;
        string summary;

        if (!isWin11)
        {
            shellArch = "Windows 10 经典 Ribbon 功能区";
            summary = "当前操作系统非 Windows 11，原生即为传统 Ribbon 架构。";
            details.Add("· [环境说明] 系统内核版本低于 22000，原生仅支持 Win30/Win10 传统 ExplorerFrame 界面。");
        }
        else if (hasAnomaly)
        {
            shellArch = "经典 Ribbon 降级模式 (Win10 回退)";
            var issues = new List<string>();
            if (modernBlocked) issues.Add("现代 Fluent 命令栏被注册表屏蔽");
            if (separateProcess) issues.Add("开启了独立进程导致多标签页脱轨");
            summary = $"检测到 {issues.Count} 项异常导致外壳降级：{string.Join("，", issues)}。";

            if (modernBlocked)
                details.Add($"· [阻断证据] 注册表命中 CLSID {Win11ModernCommandBarClsid}，强制屏蔽了 Windows 11 现代 Fluent 命令栏。");
            if (separateProcess)
                details.Add("· [设置冲突] Advanced\\SeparateProcess 为 1，多标签页宿主无法承载新建窗口，自动降级为 Win10 独立窗口。");
        }
        else
        {
            shellArch = "Windows 11 现代 Fluent 多标签页 (XAML)";
            summary = "系统外壳健康！当前处于纯正的 Windows 11 原生现代多标签页与 Fluent 界面。";
            details.Add("· [外壳纯净] 现代 Fluent 命令栏 CLSID 未被拦截，各项参数正常。");
            details.Add("· [进程策略] 统一单进程宿主，标签页无缝承载。");
        }

        if (isElevated)
        {
            details.Add("· [权限提示] 当前检测程序以管理员权限运行。在 Windows 11 下，若直接调用 explorer.exe，系统出于隔离安全亦可能临时降级唤出 Win10 窗口。");
        }

        return new ExplorerDiagnosisResult(
            hasAnomaly,
            isWin11,
            modernBlocked,
            blockedLocations,
            separateProcess,
            shellArch,
            summary,
            details,
            blockedDisplay,
            sepDisplay,
            sysVersionDisplay,
            integrityDisplay);
    }

    /// <summary>
    /// 一键安全修复资源管理器外壳设置，恢复 Windows 11 纯正原生状态
    /// </summary>
    public static ExplorerRepairResult Repair()
    {
        var actions = new List<string>();

        try
        {
            // 1. 清理 HKCU Blocked 项
            using (var hkcu = Registry.CurrentUser.OpenSubKey(ShellExtensionsBlockedKey, true))
            {
                if (hkcu != null && hkcu.GetValue(Win11ModernCommandBarClsid) != null)
                {
                    hkcu.DeleteValue(Win11ModernCommandBarClsid, false);
                    actions.Add($"已从 HKCU 注册表中移除对现代命令栏 ({Win11ModernCommandBarClsid}) 的屏蔽项。");
                }
            }

            // 2. 清理 HKLM Blocked 项（需管理员权限，静默尝试）
            try
            {
                using var hklm = Registry.LocalMachine.OpenSubKey(ShellExtensionsBlockedKey, true);
                if (hklm != null && hklm.GetValue(Win11ModernCommandBarClsid) != null)
                {
                    hklm.DeleteValue(Win11ModernCommandBarClsid, false);
                    actions.Add("已从 HKLM 机器级注册表中移除对现代命令栏的屏蔽项。");
                }
            }
            catch (UnauthorizedAccessException)
            {
                actions.Add("提示: HKLM 机器级注册表需要管理员提权方可修改，当前用户级设置已优先修复。");
            }

            // 3. 重置 SeparateProcess 为 0
            using (var adv = Registry.CurrentUser.CreateSubKey(ExplorerAdvancedKey, true))
            {
                if (adv != null)
                {
                    var cur = adv.GetValue("SeparateProcess", 0);
                    if (Convert.ToInt32(cur) != 0)
                    {
                        adv.SetValue("SeparateProcess", 0, RegistryValueKind.DWord);
                        actions.Add("已将【在单独的进程中打开文件夹窗口】(SeparateProcess) 恢复为 0 (单进程共享)。");
                    }
                }
            }

            // 4. 广播外壳设置变更通知（绝不暴力杀 explorer.exe）
            try
            {
                SendMessageTimeout(
                    HWND_BROADCAST,
                    WM_SETTINGCHANGE,
                    UIntPtr.Zero,
                    "Shell",
                    SMTO_ABORTIFHUNG,
                    1500,
                    out _);
                actions.Add("已向 Windows 桌面外壳广播 WM_SETTINGCHANGE 设置刷新事件。");
            }
            catch (Exception ex)
            {
                actions.Add($"广播通知调用异常（不影响设置保存）: {ex.Message}");
            }

            if (actions.Count == 0)
            {
                actions.Add("未检测到需要修复的异常项，系统外壳已处于健康原生状态。");
            }

            return new ExplorerRepairResult(true, actions, null, true);
        }
        catch (Exception ex)
        {
            return new ExplorerRepairResult(false, actions, ex.Message, false);
        }
    }
}

