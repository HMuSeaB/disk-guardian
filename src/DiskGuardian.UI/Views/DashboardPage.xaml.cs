using System;
using System.IO;
using System.Threading.Tasks;
using DiskGuardian.Core.Diagnostics;
using DiskGuardian.Core.Safety;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DiskGuardian.UI.Views;

public partial class DashboardPage : Page
{
    public DashboardPage()
    {
        this.InitializeComponent();
        RefreshDrives();
        _ = RefreshProxyStatusAsync();
    }

    private void RefreshDrives()
    {
        try
        {
            var driveC = new DriveInfo("C");
            if (driveC.IsReady)
            {
                long free = driveC.AvailableFreeSpace;
                long total = driveC.TotalSize;
                int pct = (int)((total - free) * 100 / total);
                CProgress.Value = pct;
                CText.Text = $"可用: {free / (1024 * 1024 * 1024.0):F2} GiB / {total / (1024 * 1024 * 1024.0):F2} GiB (已用 {pct}%)";
            }

            var driveD = new DriveInfo("D");
            if (driveD.IsReady)
            {
                long free = driveD.AvailableFreeSpace;
                long total = driveD.TotalSize;
                int pct = (int)((total - free) * 100 / total);
                DProgress.Value = pct;
                DText.Text = $"可用: {free / (1024 * 1024 * 1024.0):F2} GiB / {total / (1024 * 1024 * 1024.0):F2} GiB (已用 {pct}%)";
            }
        }
        catch { }
    }

    private async Task RefreshProxyStatusAsync()
    {
        try
        {
            var report = await Task.Run(() => ProxyDoctor.Diagnose());

            ProxySummaryText.Text = report.Summary;

            // 1. 顶部指示灯与 Badge
            if (report.HasAnomaly)
            {
                ProxyStatusIcon.Glyph = "\uE7BA"; // Warning icon
                ProxyStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
                ProxyBadgeText.Text = "幽灵残留告警";
            }
            else
            {
                ProxyStatusIcon.Glyph = "\uE73E"; // Checkmark icon
                ProxyStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                ProxyBadgeText.Text = report.ProxyEnable ? "活跃代理" : "健康直连";
            }

            // 2. 四宫格指标 (Metric Tiles)
            // Tile 1: 工作模式
            ProxyModeValueText.Text = report.ProxyTypeDisplay;
            ProxyModeSubText.Text = report.ProxyEnable ? "系统级代理已开启" : (report.InconsistentFlags ? "底层残留手动标记" : "网络流量直接通信");

            // Tile 2: 目标服务器与端口
            if (!string.IsNullOrEmpty(report.ProxyServer))
            {
                ProxyEndpointValueText.Text = report.ProxyServer;
                ProxyEndpointSubText.Text = report.ProxyPort.HasValue && ProxyDoctor.IsLoopbackHost(report.ProxyHost) ? "本机回环代理" : "外部代理目标";
            }
            else
            {
                ProxyEndpointValueText.Text = "直连互联网";
                ProxyEndpointSubText.Text = "无需代理转发";
            }

            // Tile 3: 本地端口连通性
            ProxyPortHealthValueText.Text = report.PortStatusDisplay;
            ProxyPortHealthSubText.Text = report.PortLatencyMs.HasValue ? $"TCP 探测耗时: {report.PortLatencyMs}ms" : "无本地代理监听需求";

            // Tile 4: WinINET 标志位
            ProxyFlagsValueText.Text = report.BinaryFlagHex;
            ProxyFlagsSubText.Text = report.BinaryFlagDescription;

            // 3. 折叠面板取证细节
            DetailProxyEnableText.Text = report.ProxyEnable ? "1 (已开启)" : "0 (已关闭)";
            DetailProxyOverrideText.Text = string.IsNullOrEmpty(report.ProxyOverride) ? "(未设置绕过)" : report.ProxyOverride;
            DetailPacText.Text = string.IsNullOrEmpty(report.AutoConfigUrl) ? "(未配置 PAC 脚本)" : report.AutoConfigUrl;
            DetailEnvProxyText.Text = report.EnvStatusDisplay;
            DetailGitProxyText.Text = report.GitStatusDisplay;

            if (report.WorkBuddyDetected)
            {
                DetailAppHardeningLabel.Text = "WorkBuddy 隔离防线:";
                DetailAppHardeningText.Text = report.WorkBuddyStatusDisplay;
            }
            else
            {
                DetailAppHardeningLabel.Text = "已识别应用安全隔离:";
                DetailAppHardeningText.Text = report.AppHardeningDisplay;
            }

            if (report.IdeList.Count > 0)
            {
                var ideLines = report.IdeList.Select(i => $"• [{i.Name}] {i.StatusDisplay}\n  路径: {i.SettingsPath}");
                DetailIdeListText.Text = string.Join("\n", ideLines);
            }
            else
            {
                DetailIdeListText.Text = "本机未检测到 VS Code / Cursor / Trae 等 Agent IDE 默认配置目录。";
            }

            if (report.Details.Count > 0)
            {
                ProxyDetailLogsText.Text = "• " + string.Join("\n• ", report.Details);
            }
            else
            {
                ProxyDetailLogsText.Text = "WinINET 注册表标志位一致，无死端口，环境变量与 Git 配置纯净。";
            }
        }
        catch (Exception ex)
        {
            ProxySummaryText.Text = $"体检异常: {ex.Message}";
        }
    }

    private async void HardenIdeProxy_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "加固主流 Agent IDE 代理防线",
            Content = "将为您自动探测本机已安装的 VS Code, Cursor, Trae, WorkBuddy, Windsurf 等 IDE，安全注入：\n\n1. http.proxySupport: \"on\"\n2. http.proxy: 绑定当前活跃代理 (如 127.0.0.1:7890)\n\n解决自定义海外模型（如 DeepSeek、Claude、OpenAI、TokenHarbor 等）因缺少代理设置而走直连断网的问题。\n原配置文件将自动生成 .bak 备份。\n\n是否立即加固？",
            PrimaryButtonText = "立即加固",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var (count, logs) = await Task.Run(() => ProxyDoctor.HardenAllIdeProxies());
        var tip = new ContentDialog
        {
            Title = "加固完成",
            Content = $"已成功加固 {count} 款 Agent IDE：\n\n✓ " + string.Join("\n✓ ", logs) + "\n\n重启对应 IDE 后，自定义模型即可顺畅连通！",
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await tip.ShowAsync();
        await RefreshProxyStatusAsync();
    }

    private async void CreateShortcut_Click(object sender, RoutedEventArgs e)
    {
        var (ok, path, err) = ShortcutManager.CreateDesktopShortcut();
        if (ok)
        {
            var tip = new ContentDialog
            {
                Title = "桌面快捷方式创建成功",
                Content = $"已成功在您的 Windows 桌面上生成规范快捷方式：\n\n{path}\n\n您现在可以随时从桌面双击直接启动 DiskGuardian。",
                CloseButtonText = "好的",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        else
        {
            var tip = new ContentDialog
            {
                Title = "创建快捷方式失败",
                Content = $"未能生成桌面快捷方式：\n{err}",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
    }

    private async void RefreshProxy_Click(object sender, RoutedEventArgs e)
    {
        await RefreshProxyStatusAsync();
    }

    private async void FixProxy_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "确认安全修复系统代理",
            Content = "将为您执行以下安全修复步骤：\n\n1. 自动备份当前注册表原值至 out/ 目录（可随时还原）；\n2. 清洗 DefaultConnectionSettings 二进制标志位为纯直连；\n3. 通过 WinINet API 向操作系统广播刷新（绝不杀 explorer.exe）；\n4. 清理残留的用户环境变量并在识别到下游应用时加固防线。\n\n是否立即执行？",
            PrimaryButtonText = "确认修复",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await confirm.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var repairResult = await Task.Run(() => ProxyDoctor.Repair(hardenWorkBuddy: true));
        if (repairResult.Succeeded)
        {
            var actionsStr = string.Join("\n✓ ", repairResult.ActionsTaken);
            var tip = new ContentDialog
            {
                Title = "修复成功",
                Content = $"已成功清洗并恢复网络配置！\n\n✓ {actionsStr}\n\n备份已保存至:\n{repairResult.BackupPath}",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }
        else
        {
            var tip = new ContentDialog
            {
                Title = "修复遇到问题",
                Content = repairResult.ErrorMessage,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
        }

        await RefreshProxyStatusAsync();
    }
}
