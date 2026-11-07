using System;
using System.IO;
using System.Linq;
using DiskGuardian.Core.Discovery;
using DiskGuardian.Core.Engine;
using DiskGuardian.Core.Safety;

namespace DiskGuardian.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Contains("--check-rules"))
        {
            return RunCheckRules();
        }

        int level = 1;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--level" && i + 1 < args.Length && int.TryParse(args[i + 1], out var parsedLevel))
            {
                level = Math.Clamp(parsedLevel, 1, 3);
            }
        }

        bool apply = args.Contains("--apply");
        bool purge = args.Contains("--purge");

        if (purge)
        {
            Console.WriteLine("=== disk-guardian 清空回收站 (安全防卡死模式) ===");
            var (count, failed) = RecycleManager.EmptySafely();
            Console.WriteLine($"已成功清理 {count} 个文件/目录项。失败: {failed.Count} 项。");
            return failed.Count > 0 ? 1 : 0;
        }

        Console.WriteLine($"=== disk-guardian start | mode={(apply ? "apply" : "preview")} | level<={level} ===");

        try
        {
            var engine = new CleanerEngine();
            var plan = engine.BuildPlan(level);

            Console.WriteLine($"\n{"LV",-3} {"ID",-26} {"SIZE",-10} {"FILES",-6} {"STATUS",-10} NAME");
            Console.WriteLine(new string('-', 85));

            long totalReadyBytes = 0;
            foreach (var row in plan)
            {
                Console.WriteLine($"{row.Level,-3} {row.Id,-26} {row.SizeHuman,-10} {row.FilesCount,-6} {row.Status,-10} {row.Name}");
                if (row.Status == "ready") totalReadyBytes += row.SizeBytes;
            }

            Console.WriteLine(new string('-', 85));
            Console.WriteLine($"可清理合计: {CleanerEngine.HumanSize(totalReadyBytes)} (仅统计 status=ready)");

            if (!apply)
            {
                Console.WriteLine("\n[提示] 当前为只读预览模式。如需真正执行，请加参数 --apply 并输入确认令牌。");
                return 0;
            }

            Console.Write("\n警告：此操作将把可清理项移入回收站。请输入 'DELETE' 确认: ");
            var token = Console.ReadLine()?.Trim();
            if (token != "DELETE")
            {
                Console.WriteLine("令牌不匹配，已取消操作。");
                return 2;
            }

            var logFile = Path.Combine(engine.RootDir, "out", $"cli_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            using var logger = new AuditLogger(logFile);
            var (freed, count, failed) = engine.ExecutePlan(plan, plan.Where(r => r.Status == "ready").Select(r => r.Id), logger);

            Console.WriteLine($"\n清理完成！共释放空间: {CleanerEngine.HumanSize(freed)}，处理文件: {count} 个。失败: {failed.Count} 个。");
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"运行失败: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static int RunCheckRules()
    {
        Console.WriteLine("=== disk-guardian C# 核心自检 | mode=check | level<=1 ===");
        Console.WriteLine("--- HARD_DENY self-test (18 条安全断言) ---");

        var (hardOk, hardLogs) = HardDeny.RunSelfTest();
        foreach (var l in hardLogs)
        {
            Console.WriteLine(l);
        }

        Console.WriteLine("\n--- ProjectDiscoverer self-test (3 条发现断言) ---");
        var (discOk, discLogs) = ProjectDiscoverer.RunDiscoverSelfTest();
        foreach (var l in discLogs)
        {
            Console.WriteLine(l);
        }

        Console.WriteLine("--- end self-test ---\n");

        if (hardOk && discOk)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("ALL 21 SELF-TEST ASSERTIONS PASSED! 核心安全与防误杀机制运行正常。");
            Console.ResetColor();
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("SELF-TEST FAILED! 存在安全缺陷，请检查阻断逻辑。");
        Console.ResetColor();
        return 1;
    }
}
