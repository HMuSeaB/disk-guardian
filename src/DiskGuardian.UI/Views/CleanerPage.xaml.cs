using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DiskGuardian.Core.Engine;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DiskGuardian.UI.Views;

public partial class CleanerPage : Page
{
    private readonly CleanerEngine _engine;
    private List<PlanRow> _currentPlan = new();

    public CleanerPage()
    {
        this.InitializeComponent();
        _engine = new CleanerEngine();
        _ = RefreshPlanAsync(1);
    }

    private async Task RefreshPlanAsync(int level)
    {
        ScanRing.IsActive = true;
        try
        {
            _currentPlan = await Task.Run(() => _engine.BuildPlan(level));
            RulesList.ItemsSource = _currentPlan;
            UpdateTotal();
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "扫描失败",
                Content = ex.Message,
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            _ = dialog.ShowAsync();
        }
        finally
        {
            ScanRing.IsActive = false;
        }
    }

    private void UpdateTotal()
    {
        long sum = _currentPlan.Where(r => r.IsSelected && r.Status == "ready").Sum(r => r.SizeBytes);
        TotalSizeText.Text = CleanerEngine.HumanSize(sum);
    }

    private void LevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LevelCombo == null) return;
        int level = LevelCombo.SelectedIndex + 1;
        _ = RefreshPlanAsync(level);
    }

    private void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        int level = LevelCombo.SelectedIndex + 1;
        _ = RefreshPlanAsync(level);
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = _currentPlan.Where(r => r.IsSelected && r.Status == "ready").ToList();
        if (selected.Count == 0)
        {
            var tip = new ContentDialog
            {
                Title = "未勾选清理项",
                Content = "当前没有选中有可清理内容的有效规则项。",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
            return;
        }

        var confirmDialog = new ContentDialog
        {
            Title = "确认安全清理",
            Content = $"即将把 {selected.Count} 项共 {TotalSizeText.Text} 移入回收站（可随时还原）。是否继续？",
            PrimaryButtonText = "确认清理",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await confirmDialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        ScanRing.IsActive = true;
        try
        {
            var logPath = Path.Combine(_engine.RootDir, "out", $"ui_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            using var logger = new AuditLogger(logPath);

            var (freed, count, failed) = await Task.Run(() =>
                _engine.ExecutePlan(_currentPlan, selected.Select(s => s.Id), logger)
            );

            var successDialog = new ContentDialog
            {
                Title = "清理完成",
                Content = $"已安全移入回收站！共释放空间 {CleanerEngine.HumanSize(freed)}，处理 {count} 个文件。\n失败: {failed.Count} 项。",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await successDialog.ShowAsync();

            int level = LevelCombo.SelectedIndex + 1;
            await RefreshPlanAsync(level);
        }
        finally
        {
            ScanRing.IsActive = false;
        }
    }
}
