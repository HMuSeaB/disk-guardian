using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DiskGuardian.Core.Engine;
using DiskGuardian.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DiskGuardian.UI.Views;

public partial class CleanerPage : Page
{
    private readonly CleanerEngine _engine;
    private readonly ObservableCollection<CleanerRuleRow> _ruleRows = new();
    private bool _isScanning = false;

    public CleanerPage()
    {
        this.InitializeComponent();
        _engine = new CleanerEngine();

        // 页面载入时仅预载规则元数据，绝不自动触发深层磁盘遍历
        InitializeRuleList();
    }

    private void InitializeRuleList()
    {
        _ruleRows.Clear();
        foreach (var rule in _engine.Config.Rules)
        {
            // 默认仅勾选 Level 1 快速无痛项，L2/L3 需用户手动确认勾选
            var row = new CleanerRuleRow(rule, isSelectedDefault: rule.Level == 1);
            row.SelectionChanged += (s, e) => UpdateTotal();
            _ruleRows.Add(row);
        }

        RulesList.ItemsSource = _ruleRows;
        UpdateTotal();
    }

    private void UpdateTotal()
    {
        long sum = _ruleRows
            .Where(r => r.IsSelected && r.ScannedResult != null && r.ScannedResult.Status == "ready")
            .Sum(r => r.SizeBytes);
        TotalSizeText.Text = CleanerEngine.HumanSize(sum);
    }

    private void LevelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LevelCombo == null || _ruleRows.Count == 0 || _isScanning) return;
        int level = LevelCombo.SelectedIndex + 1;

        // 仅切换预设勾选项，坚决不自动触发任何后台扫描
        foreach (var row in _ruleRows)
        {
            row.IsSelected = row.Level <= level;
        }

        int selectedCount = _ruleRows.Count(r => r.IsSelected);
        ScanStatusText.Text = $"已勾选 Level {level} 范围共 {selectedCount} 项，请点击【开始扫描】";
        UpdateTotal();
    }

    private void SelectL1_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanning) return;
        foreach (var row in _ruleRows) row.IsSelected = row.Level == 1;
        UpdateTotal();
        ScanStatusText.Text = "已勾选仅 Level 1 快速缓存项";
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanning) return;
        foreach (var row in _ruleRows) row.IsSelected = true;
        UpdateTotal();
        ScanStatusText.Text = $"已全选共 {_ruleRows.Count} 项";
    }

    private void DeselectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanning) return;
        foreach (var row in _ruleRows) row.IsSelected = false;
        UpdateTotal();
        ScanStatusText.Text = "已清空所有勾选项";
    }

    private async void ScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanning) return;

        var toScan = _ruleRows.Where(r => r.IsSelected).ToList();
        if (toScan.Count == 0)
        {
            var tip = new ContentDialog
            {
                Title = "未选择扫描项",
                Content = "请至少勾选一个需要扫描的规则选项。",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
            return;
        }

        _isScanning = true;
        ScanRing.IsActive = true;
        ScanButton.IsEnabled = false;

        try
        {
            int current = 0;
            foreach (var row in toScan)
            {
                current++;
                ScanStatusText.Text = $"正在扫描 ({current}/{toScan.Count}): {row.Name}...";
                row.MarkScanning();

                // 单项按需异步扫描，绝不牵扯未勾选项目
                var result = await Task.Run(() => _engine.ScanRule(row.Rule));
                row.ApplyResult(result);
                UpdateTotal();
            }

            long totalScannedBytes = _ruleRows
                .Where(r => r.IsSelected && r.ScannedResult != null && r.ScannedResult.Status == "ready")
                .Sum(r => r.SizeBytes);
            ScanStatusText.Text = $"扫描完成！共检查 {toScan.Count} 项，发现可清理 {CleanerEngine.HumanSize(totalScannedBytes)}";
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"扫描失败: {ex.Message}";
        }
        finally
        {
            _isScanning = false;
            ScanRing.IsActive = false;
            ScanButton.IsEnabled = true;
            UpdateTotal();
        }
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isScanning) return;

        var selected = _ruleRows
            .Where(r => r.IsSelected && r.ScannedResult != null && r.ScannedResult.Status == "ready" && r.SizeBytes > 0)
            .ToList();

        if (selected.Count == 0)
        {
            var tip = new ContentDialog
            {
                Title = "无待清理项",
                Content = "当前勾选的规则中没有已扫描且体积大于 0 的可清理项。\n请先勾选需要清理的规则并点击【开始扫描勾选项】。",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await tip.ShowAsync();
            return;
        }

        var confirmDialog = new ContentDialog
        {
            Title = "确认安全清理",
            Content = $"即将把选中的 {selected.Count} 个规则共计 {TotalSizeText.Text} 安全移入回收站（可随时撤销还原）。\n\n是否继续？",
            PrimaryButtonText = "确认清理",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await confirmDialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        ScanRing.IsActive = true;
        ApplyButton.IsEnabled = false;
        ScanStatusText.Text = "正在将选中项移入回收站...";

        try
        {
            var planToExecute = selected.Select(s => s.ScannedResult!).ToList();
            var logPath = Path.Combine(_engine.RootDir, "out", $"ui_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            using var logger = new AuditLogger(logPath);

            var (freed, count, failed) = await Task.Run(() =>
                _engine.ExecutePlan(planToExecute, selected.Select(s => s.Id), logger)
            );

            // 清理完成后更新界面对应行的状态
            foreach (var row in selected)
            {
                row.ApplyResult(new PlanRow
                {
                    Id = row.Id,
                    Name = row.Name,
                    Level = row.Level,
                    SizeBytes = 0,
                    SizeHuman = "0 B",
                    Status = "clean"
                });
            }
            UpdateTotal();

            var successDialog = new ContentDialog
            {
                Title = "清理完成",
                Content = $"已安全移入回收站！共释放空间 {CleanerEngine.HumanSize(freed)}，处理 {count} 个文件/目录。\n失败: {failed.Count} 项。",
                CloseButtonText = "确定",
                XamlRoot = this.XamlRoot
            };
            await successDialog.ShowAsync();

            ScanStatusText.Text = $"清理完成，成功释放 {CleanerEngine.HumanSize(freed)}";
        }
        catch (Exception ex)
        {
            ScanStatusText.Text = $"清理失败: {ex.Message}";
        }
        finally
        {
            ScanRing.IsActive = false;
            ApplyButton.IsEnabled = true;
        }
    }
}
