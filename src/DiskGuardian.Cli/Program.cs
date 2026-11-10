using System;
using System.IO;
using System.Linq;
using DiskGuardian.Core.Diagnostics;
using DiskGuardian.Core.Discovery;
using DiskGuardian.Core.Engine;
using DiskGuardian.Core.Safety;

namespace DiskGuardian.Cli;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Contains("--create-shortcut"))
        {
            var (ok, path, err) = ShortcutManager.CreateDesktopShortcut();
            if (ok)
            {
                Console.WriteLine($"[成功] 已在桌面创建快捷方式: {path}");
                return 0;
            }
            Console.WriteLine($"[失败] 创建桌面快捷方式失败: {err}");
            return 1;
        }

        if (args.Contains("--check-rules"))
        {
            return RunCheckRules();
        }

        if (args.Contains("--check-proxy"))
        {
            return RunCheckProxy();
        }

        if (args.Contains("--fix-proxy"))
        {
            return RunFixProxy();
        }

        if (args.Contains("--harden-ide-proxy"))
        {
            return RunHardenIdeProxy();
        }

        if (args.Contains("--check-shell"))
        {
            return RunCheckShell();
        }

        if (args.Contains("--fix-shell"))
        {
            return RunFixShell();
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

    private static int RunCheckProxy()
    {
        Console.WriteLine("=== disk-guardian 系统网络与幽灵代理体检 (ProxyDoctor) ===\n");
        var report = ProxyDoctor.Diagnose();
        Console.WriteLine($"【总体结论】: {report.Summary}\n");

        Console.WriteLine("--- 代理核心配置概览 ---");
        Console.WriteLine($"  * 工作模式:     {report.ProxyTypeDisplay}");
        Console.WriteLine($"  * 服务器地址:   {(string.IsNullOrEmpty(report.ProxyHost) ? "(无)" : report.ProxyHost)}");
        Console.WriteLine($"  * 代理端口:     {(report.ProxyPort.HasValue ? report.ProxyPort.Value.ToString() : "(无)")}");
        Console.WriteLine($"  * 本地端口状态: {report.PortStatusDisplay}");
        Console.WriteLine($"  * WinINET标志:  {report.BinaryFlagDescription}");
        if (!string.IsNullOrEmpty(report.AutoConfigUrl))
            Console.WriteLine($"  * PAC 脚本:     {report.AutoConfigUrl}");
        if (!string.IsNullOrEmpty(report.ProxyOverride))
            Console.WriteLine($"  * 绕过名单:     {report.ProxyOverride}");

        Console.WriteLine("\n--- 下游环境与主流 Agent IDE 防线 ---");
        Console.WriteLine($"  * 用户环境变量: {report.EnvStatusDisplay}");
        Console.WriteLine($"  * Git 全局代理: {report.GitStatusDisplay}");
        Console.WriteLine($"  * IDE 代理综合: {report.AppHardeningDisplay}");

        if (report.IdeList.Count > 0)
        {
            Console.WriteLine("\n--- 已识别 Agent IDE 独立诊断 ---");
            foreach (var ide in report.IdeList)
            {
                Console.WriteLine($"  [{ide.Name}] {ide.StatusDisplay}");
                Console.WriteLine($"     路径: {ide.SettingsPath}");
            }
        }

        if (report.Details.Count > 0)
        {
            Console.WriteLine("\n--- 详细技术排查诊断取证 ---");
            foreach (var d in report.Details)
            {
                Console.WriteLine($"  - {d}");
            }
        }

        Console.WriteLine("\n" + (report.HasAnomaly ? "【告警】检测到异常！建议执行: disk-guardian --fix-proxy 进行一键安全清洗。" : "【健康】网络与代理状态良好，未发现幽灵残留冲突。"));
        return report.HasAnomaly ? 1 : 0;
    }

    private static int RunFixProxy()
    {
        Console.WriteLine("=== disk-guardian 幽灵代理安全清洗与修复 ===\n");
        var res = ProxyDoctor.Repair(hardenWorkBuddy: true);
        if (res.Succeeded)
        {
            Console.WriteLine("[修复成功] 已执行以下安全恢复步骤:");
            foreach (var act in res.ActionsTaken)
            {
                Console.WriteLine($"  ✓ {act}");
            }
            Console.WriteLine($"\n系统设置已向操作系统全量广播生效。备份文件保存在:\n  {res.BackupPath}");
            return 0;
        }
        else
        {
            Console.WriteLine($"[修复失败]: {res.ErrorMessage}");
            return 1;
        }
    }

    private static int RunHardenIdeProxy()
    {
        Console.WriteLine("=== disk-guardian 主流 Agent IDE 代理防线一键加固 ===\n");
        Console.WriteLine("正在探测 VS Code, Cursor, Trae, WorkBuddy, Windsurf 等并注入 proxySupport 与活跃代理...\n");

        var (count, logs) = ProxyDoctor.HardenAllIdeProxies();
        foreach (var l in logs)
        {
            Console.WriteLine($"  ✓ {l}");
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[加固完成] 共为 {count} 款已安装的 Agent IDE 成功开启代理支持与本地绑定！");
        Console.WriteLine("重启对应 IDE 后，自定义海外模型（DeepSeek、Claude、OpenAI、TokenHarbor 等）即可顺畅连通。");
        Console.ResetColor();
        return 0;
    }

    private static int RunCheckShell()
    {
        Console.WriteLine("=== disk-guardian 桌面外壳与资源管理器健康体检 (ExplorerDoctor) ===\n");
        var report = ExplorerDoctor.Diagnose();
        Console.WriteLine($"【总体结论】: {report.Summary}\n");

        Console.WriteLine("--- 外壳与资源管理器核心指标 ---");
        Console.WriteLine($"  * 系统内核版本:     {report.SystemVersionDisplay}");
        Console.WriteLine($"  * 当前外壳架构:     {report.ShellArchitecture}");
        Console.WriteLine($"  * 现代Fluent命令栏: {report.BlockedStatusDisplay}");
        Console.WriteLine($"  * 窗口多进程策略:   {report.SeparateProcessDisplay}");
        Console.WriteLine($"  * 运行权限环境:     {report.IntegrityStatusDisplay}");

        if (report.Details.Count > 0)
        {
            Console.WriteLine("\n--- 底层排查诊断取证 ---");
            foreach (var d in report.Details)
            {
                Console.WriteLine($"  {d}");
            }
        }

        Console.WriteLine();
        if (report.HasAnomaly)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("【建议】检测到系统外壳降级/阻断风险！可执行: disk-guardian --fix-shell 进行一键安全恢复。");
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("【健康】资源管理器配置纯正，处于 Windows 11 原生多标签页与现代 Fluent 状态。");
        Console.ResetColor();
        return 0;
    }

    private static int RunFixShell()
    {
        Console.WriteLine("=== disk-guardian 恢复 Windows 11 原生资源管理器外壳 ===\n");
        var res = ExplorerDoctor.Repair();
        if (res.Succeeded)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[修复成功] 已执行以下安全恢复步骤:");
            Console.ResetColor();
            foreach (var act in res.ActionsTaken)
            {
                Console.WriteLine($"  ✓ {act}");
            }

            Console.WriteLine("\n[生效说明]:");
            Console.WriteLine("  1. 已通过 Win32 API 广播外壳刷新通知（零黑框、绝不暴力杀 explorer.exe）。");
            Console.WriteLine("  2. 新打开的资源管理器窗口（Win + E）将即刻生效 Win11 现代标签页。");
            Console.WriteLine("  3. 若需当前旧窗口立即变更为新版，可在任务管理器中右键【Windows 资源管理器】点击【重新启动】。");
            return 0;
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[修复失败]: {res.ErrorMessage}");
            Console.ResetColor();
            return 1;
        }
    }
}
