using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DiskGuardian.Core.Discovery;

public sealed record DiscoverSpec(
    IReadOnlyList<string> Roots,
    string DirName,
    string? RequireSibling = null,
    int MaxDepth = 5,
    IReadOnlyList<string>? ExcludeContains = null
);

/// <summary>
/// 高性能项目动态发现器：利用 System.IO.Enumeration 递归发现构建产物，同时严格防御 Electron 应用自损。
/// </summary>
public static class ProjectDiscoverer
{
    private static readonly ConcurrentDictionary<string, List<string>> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static List<string> DiscoverPaths(DiscoverSpec spec, IReadOnlyDictionary<string, string> variables)
    {
        if (string.IsNullOrWhiteSpace(spec.DirName)) return [];

        var expandedRoots = spec.Roots
            .Select(r => Safety.HardDeny.Expand(r, variables))
            .Where(Directory.Exists)
            .ToList();

        var excludes = (spec.ExcludeContains ?? [])
            .Select(e => e.ToLowerInvariant())
            .ToList();

        var targetName = spec.DirName.Trim().ToLowerInvariant();
        var cacheKey = $"{string.Join(";", expandedRoots)}|{targetName}|{spec.RequireSibling}|{spec.MaxDepth}|{string.Join(";", excludes)}";

        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            return new List<string>(cached);
        }

        var found = new List<string>();

        foreach (var root in expandedRoots)
        {
            Walk(root, 0, targetName, spec.RequireSibling, spec.MaxDepth, excludes, found);
        }

        found.Sort(StringComparer.OrdinalIgnoreCase);
        Cache[cacheKey] = found;
        return new List<string>(found);
    }

    private static void Walk(
        string currentDir,
        int currentDepth,
        string targetDirName,
        string? requireSibling,
        int maxDepth,
        List<string> excludes,
        List<string> results)
    {
        if (currentDepth > maxDepth) return;

        try
        {
            var dirInfo = new DirectoryInfo(currentDir);
            // 跳过重解析点（符号链接/连接点），防死循环
            if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;

            var subDirs = dirInfo.EnumerateDirectories();

            foreach (var sub in subDirs)
            {
                var fullPath = sub.FullName;
                var low = fullPath.ToLowerInvariant();

                // 排除列表防御（如 resources\app 核心运行时）
                if (excludes.Any(low.Contains)) continue;

                // 跳过子目录重解析点
                if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

                if (sub.Name.Equals(targetDirName, StringComparison.OrdinalIgnoreCase))
                {
                    // 检查同级清单文件是否存在
                    if (!string.IsNullOrEmpty(requireSibling))
                    {
                        var siblingPath = Path.Combine(currentDir, requireSibling);
                        if (!File.Exists(siblingPath))
                        {
                            continue; // 虽叫 target/node_modules，但没有同级清单，绝不误认
                        }
                    }

                    results.Add(fullPath);
                    continue; // 匹配到的构建目录不再向下递归
                }

                if (currentDepth < maxDepth)
                {
                    Walk(fullPath, currentDepth + 1, targetDirName, requireSibling, maxDepth, excludes, results);
                }
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
    }

    /// <summary>
    /// 发现器回归自测：验证能否正确识别项目并精确排除 Electron 安装本体（防软件误杀惨剧）。
    /// </summary>
    public static (bool Success, List<string> Logs) RunDiscoverSelfTest()
    {
        var logs = new List<string>();
        var tempRoot = Path.Combine(Path.GetTempPath(), "dg_discover_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);

            // 1. 合法项目目录
            var proj = Path.Combine(tempRoot, "my-app");
            Directory.CreateDirectory(Path.Combine(proj, "node_modules"));
            File.WriteAllText(Path.Combine(proj, "package.json"), "{}");

            // 2. Electron 软件本体依赖 (Cursor / Photoshop / WorkBuddy 结构)
            var electronApp = Path.Combine(tempRoot, "IDE", "resources", "app");
            Directory.CreateDirectory(Path.Combine(electronApp, "node_modules"));
            File.WriteAllText(Path.Combine(electronApp, "package.json"), "{}");

            // 3. 无清单的裸目录
            var bare = Path.Combine(tempRoot, "bare-folder");
            Directory.CreateDirectory(Path.Combine(bare, "node_modules"));

            var spec = new DiscoverSpec(
                Roots: new[] { tempRoot },
                DirName: "node_modules",
                RequireSibling: "package.json",
                MaxDepth: 5,
                ExcludeContains: new[] { @"\resources\app" }
            );

            var hits = DiscoverPaths(spec, new Dictionary<string, string>())
                .Select(p => p.ToLowerInvariant())
                .ToList();

            var case1 = hits.Any(h => h.EndsWith(@"my-app\node_modules", StringComparison.OrdinalIgnoreCase));
            var case2 = !hits.Any(h => h.Contains(@"resources\app", StringComparison.OrdinalIgnoreCase));
            var case3 = !hits.Any(h => h.EndsWith(@"bare-folder\node_modules", StringComparison.OrdinalIgnoreCase));

            var ok = case1 && case2 && case3;

            logs.Add(case1 ? "[SELF-TEST PASS] discover: 项目合法依赖被成功发现" : "[SELF-TEST FAIL] discover: 项目合法依赖未被发现");
            logs.Add(case2 ? "[SELF-TEST PASS] discover: Electron 运行时应用被严格排除" : "[SELF-TEST FAIL] discover: 误将 Electron 运行时列入");
            logs.Add(case3 ? "[SELF-TEST PASS] discover: 无包清单目录被安全跳过" : "[SELF-TEST FAIL] discover: 误认无清单裸目录");

            return (ok, logs);
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
        }
    }
}
