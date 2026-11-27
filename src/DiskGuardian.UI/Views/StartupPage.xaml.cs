using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DiskGuardian.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace DiskGuardian.UI.Views;

public partial class StartupPage : Page
{
    private StartupReport? _report;
    private string _currentFilter = "All";
    private string _searchQuery = string.Empty;

    public StartupPage()
    {
        this.InitializeComponent();
        _ = LoadStartupAsync();
    }

    private async Task LoadStartupAsync()
    {
        RefreshBtn.IsEnabled = false;
        SummaryText.Text = "正在进行 8 维启动项与底层注入扫描...";
        try
        {
            var report = await Task.Run(() => StartupDoctor.Scan());
            _report = report;

            SummaryText.Text = report.SummaryMessage;
            RegTaskCountText.Text = $"{report.RegistryAndFolderCount + report.ScheduledTaskCount} 项";
            RegTaskSubText.Text = $"自启={report.RegistryAndFolderCount} | 计划任务={report.ScheduledTaskCount}";
            ServiceCountText.Text = $"{report.AutoServiceCount} 个";
            OptimizableCountText.Text = $"{report.OptimizableCount} 项";
            LowLevelCountText.Text = $"NSP={report.ThirdPartyNspCount} | 角标={report.ShellOverlayCount}";
            LowLevelSubText.Text = report.IsOverlayBloated
                ? "⚠️ 角标超过 15 个会拖慢 Explorer"
                : $"AppInit={report.AppInitCount} | 角标正常";

            ApplyFilters();
        }
        catch (Exception ex)
        {
            SummaryText.Text = $"扫描失败: {ex.Message}";
        }
        finally
        {
            RefreshBtn.IsEnabled = true;
        }
    }

    private void ApplyFilters()
    {
        if (_report == null) return;

        IEnumerable<StartupItem> q = _report.Items;

        q = _currentFilter switch
        {
            "Reg" => q.Where(i => i.Category == "RegistryRun" || i.Category == "StartupFolder"),
            "Svc" => q.Where(i => i.Category == "AutoService"),
            "Task" => q.Where(i => i.Category == "ScheduledTask"),
            "Low" => q.Where(i => i.Category == "WinsockNsp" || i.Category == "AppInitDll" || i.Category == "ShellOverlay"),
            "Opt" => q.Where(i => i.RiskLevel == "SafeToDisable"),
            _ => q
        };

        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            var kw = _searchQuery.Trim();
            q = q.Where(i =>
                i.Name.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                i.Command.Contains(kw, StringComparison.OrdinalIgnoreCase) ||
                i.Reason.Contains(kw, StringComparison.OrdinalIgnoreCase));
        }

        StartupListView.ItemsSource = q.ToList();
    }

    private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        await LoadStartupAsync();
    }

    private void CategoryBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked) return;

        foreach (var btn in new[] { CatAllBtn, CatRegBtn, CatSvcBtn, CatTaskBtn, CatLowBtn, CatOptBtn })
        {
            btn.IsChecked = btn == clicked;
        }

        _currentFilter = clicked.Tag?.ToString() ?? "All";
        ApplyFilters();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _searchQuery = SearchBox.Text ?? string.Empty;
        ApplyFilters();
    }

    private async void ToggleItemBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not StartupItem item) return;
        bool targetEnable = !item.IsEnabled;

        btn.IsEnabled = false;
        var (ok, msg) = await Task.Run(() => StartupDoctor.ToggleItem(item, targetEnable));
        btn.IsEnabled = true;

        var dialog = new ContentDialog
        {
            Title = ok ? "启动项状态已更新" : "操作未完成",
            Content = msg,
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();

        if (ok)
        {
            await LoadStartupAsync();
        }
    }
}
