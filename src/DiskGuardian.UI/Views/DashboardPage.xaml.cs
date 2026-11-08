using System;
using System.IO;
using System.Threading.Tasks;
using DiskGuardian.Core.Diagnostics;
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
            var c = new DriveInfo("C");
            if (c.IsReady)
            {
                var used = c.TotalSize - c.AvailableFreeSpace;
                var pct = (double)used / c.TotalSize * 100.0;
                CProgress.Value = pct;
                CText.Text = $"可用: {c.AvailableFreeSpace / (1024.0 * 1024 * 1024):F2} GiB / {c.TotalSize / (1024.0 * 1024 * 1024):F2} GiB (已用 {pct:F0}%)";
            }

            var d = new DriveInfo("D");
            if (d.IsReady)
            {
                var used = d.TotalSize - d.AvailableFreeSpace;
                var pct = (double)used / d.TotalSize * 100.0;
                DProgress.Value = pct;
                DText.Text = $"可用: {d.AvailableFreeSpace / (1024.0 * 1024 * 1024):F2} GiB / {d.TotalSize / (1024.0 * 1024 * 1024):F2} GiB (已用 {pct:F0}%)";
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
            if (report.Details.Count > 0)
            {
                ProxyDetailText.Text = string.Join(" | ", report.Details);
            }
            else
            {
                ProxyDetailText.Text = "WinINET 注册表标志位一致，无未监听的本地回环死端口。";
            }

            if (report.HasAnomaly)
            {
                ProxyStatusIcon.Glyph = "\uE7BA"; // Warning icon
                ProxyStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
                ProxyBadgeText.Text = "检测到幽灵残留";
            }
            else
            {
                ProxyStatusIcon.Glyph = "\uE73E"; // Checkmark icon
                ProxyStatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
                ProxyBadgeText.Text = report.ProxyEnable ? "活跃代理" : "健康直连";
            }
        }
        catch (Exception ex)
        {
            ProxySummaryText.Text = $"体检异常: {ex.Message}";
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
            Content = "将为您执行以下安全修复步骤：\n\n1. 自动备份当前注册表原值至 out/ 目录（可随时还原）；\n2. 清洗 DefaultConnectionSettings 二进制标志位为纯直连；\n3. 通过 WinINet API 向操作系统广播刷新（绝不杀 explorer.exe）；\n4. 清理残留的用户环境变量并加固 WorkBuddy 防线。\n\n是否立即执行？",
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
