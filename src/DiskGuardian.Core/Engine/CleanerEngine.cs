using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using DiskGuardian.Core.Discovery;
using DiskGuardian.Core.Safety;

namespace DiskGuardian.Core.Engine;

public sealed class CleanerEngine
{
    public RuleConfig Config { get; }
    public Dictionary<string, string> Variables { get; }
    public string RootDir { get; }

    public CleanerEngine(string? rulesJsonPath = null)
    {
        var path = rulesJsonPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rules.json");
        if (!File.Exists(path))
        {
            // 向上回溯查找
            var cur = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(cur))
            {
                var candidate = Path.Combine(cur, "rules.json");
                if (File.Exists(candidate))
                {
                    path = candidate;
                    break;
                }
                cur = Path.GetDirectoryName(cur);
            }
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"找不到 rules.json 文件: {path}");
        }

        RootDir = Path.GetDirectoryName(path) ?? AppDomain.CurrentDomain.BaseDirectory;
        var json = File.ReadAllText(path);
        Config = JsonSerializer.Deserialize<RuleConfig>(json) ?? throw new InvalidOperationException("rules.json 反序列化为空");

        Variables = HardDeny.BuildDefaultVariables();
        foreach (var (k, v) in Config.Vars)
        {
            Variables[k] = v;
        }
    }

    public static bool IsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static string HumanSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{(double)bytes / (1024 * 1024 * 1024):F2} GiB";
        if (bytes >= 1024L * 1024)
            return $"{(double)bytes / (1024 * 1024):F1} MiB";
        if (bytes >= 1024L)
            return $"{(double)bytes / 1024:F0} KiB";
        return $"{bytes} B";
    }

    public List<PlanRow> BuildPlan(int maxLevel = 1)
    {
        var plan = new List<PlanRow>();
        var admin = IsAdmin();

        foreach (var rule in Config.Rules)
        {
            if (rule.Level > maxLevel) continue;

            var resolvedPaths = new List<string>();

            // 1. 固定路径解析
            if (rule.Paths != null)
            {
                foreach (var p in rule.Paths)
                {
                    try
                    {
                        resolvedPaths.Add(HardDeny.Expand(p, Variables));
                    }
                    catch { }
                }
            }

            // 2. 动态发现路径解析
            if (rule.Discover != null)
            {
                var spec = new DiscoverSpec(
                    Roots: rule.Discover.Roots,
                    DirName: rule.Discover.Dirname,
                    RequireSibling: rule.Discover.RequireSibling,
                    MaxDepth: rule.Discover.MaxDepth,
                    ExcludeContains: rule.Discover.ExcludeContains
                );

                var hits = ProjectDiscoverer.DiscoverPaths(spec, Variables);
                resolvedPaths.AddRange(hits);
            }

            // 3. 安全禁区检查
            var reasons = new List<string>();
            foreach (var p in resolvedPaths)
            {
                var r = HardDeny.CheckTarget(p, rule.AllowInsideDeny, rule.Level, Variables);
                reasons.AddRange(r);
            }

            // 4. 统计物理路径
            var existing = resolvedPaths.Where(p => Directory.Exists(p) || File.Exists(p)).ToList();
            long totalBytes = 0;
            int totalFiles = 0;
            int totalErrors = 0;

            if (reasons.Count == 0 && existing.Count > 0)
            {
                if (rule.Mode == "old-files" && rule.KeepDays > 0)
                {
                    var oldFiles = CollectOldFiles(existing, rule.KeepDays);
                    totalFiles = oldFiles.Count;
                    foreach (var f in oldFiles)
                    {
                        try { totalBytes += new FileInfo(f).Length; } catch { totalErrors++; }
                    }
                }
                else
                {
                    foreach (var p in existing)
                    {
                        var (b, f, err) = ScanTree(p);
                        totalBytes += b;
                        totalFiles += f;
                        totalErrors += err;
                    }
                }
            }

            string status;
            if (reasons.Count > 0) status = "blocked";
            else if (existing.Count == 0) status = "clean";
            else if (!admin && IsSystemRestricted(resolvedPaths)) status = "need-admin";
            else status = "ready";

            plan.Add(new PlanRow
            {
                Id = rule.Id,
                Name = rule.Name,
                Level = rule.Level,
                ResolvedPaths = resolvedPaths,
                ExistingPaths = existing,
                SizeBytes = totalBytes,
                SizeHuman = HumanSize(totalBytes),
                FilesCount = totalFiles,
                ErrorsCount = totalErrors,
                Status = status,
                BlockReasons = reasons.Distinct().ToList(),
                Rebuild = rule.Rebuild,
                Note = rule.Note,
                Mode = rule.Mode,
                KeepDays = rule.KeepDays,
                IsSelected = status == "ready"
            });
        }

        return plan;
    }

    private static bool IsSystemRestricted(IEnumerable<string> paths)
    {
        var windir = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        var normWin = HardDeny.Norm(windir);
        return paths.Any(p => HardDeny.IsAncestor(normWin, p));
    }

    public static (long Bytes, int Files, int Errors) ScanTree(string path)
    {
        long bytes = 0;
        int files = 0;
        int errors = 0;

        if (File.Exists(path))
        {
            try
            {
                return (new FileInfo(path).Length, 1, 0);
            }
            catch
            {
                return (0, 0, 1);
            }
        }

        if (!Directory.Exists(path)) return (0, 0, 0);

        var stack = new Stack<string>();
        stack.Push(path);

        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            try
            {
                var dirInfo = new DirectoryInfo(cur);
                if (dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

                foreach (var f in dirInfo.EnumerateFiles())
                {
                    try
                    {
                        bytes += f.Length;
                        files++;
                    }
                    catch
                    {
                        errors++;
                    }
                }

                foreach (var d in dirInfo.EnumerateDirectories())
                {
                    try
                    {
                        if (!d.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            stack.Push(d.FullName);
                        }
                    }
                    catch
                    {
                        errors++;
                    }
                }
            }
            catch
            {
                errors++;
            }
        }

        return (bytes, files, errors);
    }

    public static List<string> CollectOldFiles(IEnumerable<string> roots, int keepDays)
    {
        var cutoff = DateTime.Now.AddDays(-keepDays);
        var staleFiles = new List<string>();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;

            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                try
                {
                    var dir = new DirectoryInfo(cur);
                    if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;

                    foreach (var f in dir.EnumerateFiles())
                    {
                        try
                        {
                            if (f.LastWriteTime < cutoff)
                            {
                                staleFiles.Add(f.FullName);
                            }
                        }
                        catch { }
                    }

                    foreach (var d in dir.EnumerateDirectories())
                    {
                        if (!d.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        {
                            stack.Push(d.FullName);
                        }
                    }
                }
                catch { }
            }
        }

        return staleFiles;
    }

    /// <summary>
    /// 执行规则清理（安全两步删除，默认移入回收站）
    /// </summary>
    public (long FreedBytes, int DeletedFiles, List<string> Failed) ExecutePlan(
        IEnumerable<PlanRow> plan,
        IEnumerable<string> selectedRuleIds,
        AuditLogger logger)
    {
        var allowedSet = new HashSet<string>(selectedRuleIds, StringComparer.OrdinalIgnoreCase);
        long freedBytes = 0;
        int deletedFiles = 0;
        var failedList = new List<string>();

        var outDir = Path.Combine(RootDir, "out");

        foreach (var row in plan)
        {
            if (!allowedSet.Contains(row.Id)) continue;
            if (row.Status != "ready")
            {
                logger.Log($"跳过不可执行规则 {row.Id} (状态: {row.Status})");
                continue;
            }

            logger.Log($"正在执行规则 [{row.Name}] ({row.Id})...");

            if (row.Mode == "old-files" && row.KeepDays > 0)
            {
                var stale = CollectOldFiles(row.ExistingPaths, row.KeepDays);
                var (ok, failed) = RecycleManager.SendMany(stale, batchSize: 50, permanent: false);
                deletedFiles += ok.Count;
                foreach (var f in failed) failedList.Add(f.Path);
                freedBytes += row.SizeBytes;
            }
            else
            {
                foreach (var p in row.ExistingPaths)
                {
                    var (rc, exists) = RecycleManager.SendToRecycleBin(p);
                    if (rc == 0 && !exists)
                    {
                        freedBytes += row.SizeBytes;
                        deletedFiles += row.FilesCount;
                        logger.Log($"  已移入回收站: {p}");
                    }
                    else
                    {
                        failedList.Add(p);
                        logger.Log($"  移入回收站失败 (code={rc}): {p}");
                    }
                }
            }

            AuditLogger.LogAction(outDir, new
            {
                rule_id = row.Id,
                rule_name = row.Name,
                paths = row.ExistingPaths,
                freed_bytes = row.SizeBytes,
                status = "success"
            });
        }

        return (freedBytes, deletedFiles, failedList);
    }
}
