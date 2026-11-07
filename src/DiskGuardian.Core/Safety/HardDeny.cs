using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace DiskGuardian.Core.Safety;

/// <summary>
/// 核心安全底座：代码化硬禁区 (HARD_DENY)。
/// 严禁将禁区移入可编辑数据文件（如 JSON）。
/// </summary>
public static class HardDeny
{
    public const string WarnText = "此操作非常危险，可能导致不可逆的数据丢失！";

    // 绝对禁止成为删除目标的系统文件名
    public static readonly HashSet<string> ProtectedBasenames = new(StringComparer.OrdinalIgnoreCase)
    {
        "pagefile.sys", "swapfile.sys", "hiberfil.sys",
        "dedicateddumpfile.sys", "memory.dmp", "ntuser.dat",
    };

    // HARD_DENY 第 1 部分：整个子树禁止触碰（除非 allow_inside_deny=true 且 level >= 3）
    public static readonly string[] DenySubtreeTemplates =
    [
        @"%SystemRoot%\assembly",           // NGEN 镜像；误清会触发 ~2.5 GiB 后台死循环重建
        @"%SystemRoot%\WinSxS",             // 破坏更新卸载与 SFC 系统修复
        @"%SystemRoot%\Installer",          // 破坏 MSI 软件修复与卸载基准
        @"%SystemRoot%\winevt",             // 蓝屏与系统崩溃排障核心证据
        @"%SystemRoot%\System32",
        @"%SystemRoot%\SysWOW64",
        @"%SystemRoot%\Boot",
        @"%SystemRoot%\Fonts",
        @"%SystemRoot%\MEMORY.DMP",
        @"%SystemRoot%\Minidump",
        @"%ProgramFiles%",
        @"%ProgramFiles(x86)%",
        @"%USERPROFILE%\Documents",
        @"%USERPROFILE%\Desktop",
        @"%USERPROFILE%\Pictures",
        @"%USERPROFILE%\.workbuddy",        // 会话、插件与开发运行时
        @"%USERPROFILE%\.ssh",
        @"%USERPROFILE%\.gnupg",
        @"%LOCALAPPDATA%\CrashDumps",
        @"%LOCALAPPDATA%\Microsoft\Edge\User Data",
        @"C:\$Recycle.Bin",                 // 仅可由专用安全 purge 机制清理
        @"C:\Windows.old",
    ];

    // HARD_DENY 第 2 部分：这些精确路径不得删除，且任何清理目标不得为其父级（严禁删父级殃及子项）
    public static readonly string[] DenyExactTemplates =
    [
        @"%USERPROFILE%",
        @"%LOCALAPPDATA%",
        @"%APPDATA%",
        @"%SystemRoot%",
        @"%ProgramData%",
        @"%CODE_ROOT%",
        @"C:\",
        @"D:\",
        @"C:\pagefile.sys",
        @"C:\swapfile.sys",
        @"C:\hiberfil.sys",
        @"D:\pagefile.sys",
        @"D:\dedicateddumpfile.sys",
    ];

    // 自检必须拦截的危险测试路径 (15 项)
    public static readonly string[] SelfTestMustBlock =
    [
        @"%SystemRoot%\WinSxS",
        @"%SystemRoot%\assembly",
        @"%SystemRoot%\winevt\Logs",
        @"%SystemRoot%\System32",
        @"%SystemRoot%",
        @"%USERPROFILE%",
        @"%USERPROFILE%\Documents",
        @"%USERPROFILE%\Documents\anything.txt",
        @"%USERPROFILE%\.workbuddy",
        @"%LOCALAPPDATA%\CrashDumps",
        @"%ProgramFiles%",
        @"%LOCALAPPDATA%\Microsoft\Edge\User Data",
        @"C:\",
        @"C:\pagefile.sys",
        @"D:\dedicateddumpfile.sys",
    ];

    // 自检必须允许通过的安全路径 (3 项)
    public static readonly string[] SelfTestMustPass =
    [
        @"%LOCALAPPDATA%\NVIDIA\DXCache",
        @"%LOCALAPPDATA%\go-build",
        @"%SystemRoot%\Temp",
    ];

    private static readonly Regex VarRegex = new(@"%([^%]+)%", RegexOptions.Compiled);

    public static string Norm(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(path).TrimEnd('\\').ToLowerInvariant();
        }
        catch
        {
            return path.TrimEnd('\\').ToLowerInvariant();
        }
    }

    public static string Expand(string template, IReadOnlyDictionary<string, string> variables)
    {
        return VarRegex.Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            if (variables.TryGetValue(key, out var val))
            {
                return val;
            }
            throw new ArgumentException($"未知的环境变量 %{key}% 于模板 '{template}'");
        });
    }

    public static bool IsAncestor(string parent, string child)
    {
        var p = Norm(parent);
        var c = Norm(child);
        if (p == c) return true;
        return c.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase);
    }

    public static List<string> CheckTarget(string path, bool allowInsideDeny, int level, IReadOnlyDictionary<string, string> variables)
    {
        var reasons = new List<string>();
        var normPath = Norm(path);

        // 路径切分检查
        var parts = path.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !Path.IsPathRooted(path))
        {
            reasons.Add("路径层级过浅或不是绝对路径");
        }

        var fileName = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(fileName) && ProtectedBasenames.Contains(fileName))
        {
            reasons.Add("命中系统关键文件名保护");
        }

        // 扩展出实际的禁区绝对路径列表
        var subtrees = new List<string>();
        foreach (var t in DenySubtreeTemplates)
        {
            try { subtrees.Add(Norm(Expand(t, variables))); } catch { }
        }

        var exacts = new List<string>();
        foreach (var t in DenyExactTemplates)
        {
            try { exacts.Add(Norm(Expand(t, variables))); } catch { }
        }

        // 检查是否为任何禁区的祖先目录（防止删父级连带删除保护项）
        foreach (var d in exacts)
        {
            if (IsAncestor(normPath, d))
            {
                reasons.Add($"删它会连带删掉受保护项 {d}");
                break;
            }
        }

        if (reasons.Count == 0)
        {
            foreach (var d in subtrees)
            {
                if (IsAncestor(normPath, d))
                {
                    reasons.Add($"删它会连带删掉受保护项 {d}");
                    break;
                }
            }
        }

        // 检查是否位于硬禁区之内
        if (!(allowInsideDeny && level >= 3))
        {
            foreach (var d in subtrees)
            {
                if (IsAncestor(d, normPath))
                {
                    reasons.Add($"位于硬禁区 {d} 之内");
                    break;
                }
            }
        }

        return reasons;
    }

    public static Dictionary<string, string> BuildDefaultVariables()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["USERPROFILE"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ["LOCALAPPDATA"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ["APPDATA"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ["TEMP"] = Path.GetTempPath().TrimEnd('\\'),
            ["SystemRoot"] = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows",
            ["ProgramData"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ["ProgramFiles"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ["ProgramFiles(x86)"] = Environment.GetEnvironmentVariable("ProgramFiles(x86)") ?? @"C:\Program Files (x86)",
            ["CODE_ROOT"] = @"D:\4rchive\Code",
        };
    }

    /// <summary>
    /// 执行 18 项 HARD_DENY 自检断言。返回 (全部通过, 日志详情)。
    /// </summary>
    public static (bool Success, List<string> Logs) RunSelfTest(IReadOnlyDictionary<string, string>? customVars = null)
    {
        var vars = customVars ?? BuildDefaultVariables();
        var logs = new List<string>();
        var ok = true;

        foreach (var t in SelfTestMustBlock)
        {
            var p = Expand(t, vars);
            var reasons = CheckTarget(p, allowInsideDeny: false, level: 1, vars);
            if (reasons.Count == 0)
            {
                ok = false;
                logs.Add($"[SELF-TEST FAIL] 未能拦截危险目标: {p}");
            }
            else
            {
                logs.Add($"[SELF-TEST PASS] blocked  {p,-50} {reasons[0]}");
            }
        }

        foreach (var t in SelfTestMustPass)
        {
            var p = Expand(t, vars);
            var reasons = CheckTarget(p, allowInsideDeny: false, level: 1, vars);
            if (reasons.Count > 0)
            {
                ok = false;
                logs.Add($"[SELF-TEST FAIL] 误拦截合法安全目标: {p} ({string.Join("; ", reasons)})");
            }
            else
            {
                logs.Add($"[SELF-TEST PASS] allowed  {p}");
            }
        }

        return (ok, logs);
    }
}
