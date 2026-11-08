using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DiskGuardian.Core.Engine;
using DiskGuardian.Core.Safety;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DiskGuardian.UI.Views;

public partial class RecyclePage : Page
{
    public RecyclePage()
    {
        this.InitializeComponent();
        _ = LoadRecycleAsync();
    }

    private async Task LoadRecycleAsync()
    {
        try
        {
            RecycleSummaryText.Text = "正在统计回收站对象...";
            var items = await Task.Run(() => RecycleManager.EnumerateRecycleBin(new[] { @"C:\", @"D:\" }));

            long totalBytes = items.Sum(i => i.SizeBytes);

            // 当回收站包含数千个临时文件时，截取前 300 项渲染，避免 UI 假死，同时顶部如实展现全量统计
            var displayItems = items.Take(300).ToList();
            RecycleListView.ItemsSource = displayItems;

            if (items.Count > 300)
            {
                RecycleSummaryText.Text = $"{items.Count:N0} 个对象，共计 {CleanerEngine.HumanSize(totalBytes)} (已显示最新 300 项)";
            }
            else
            {
                RecycleSummaryText.Text = $"{items.Count} 个对象，共计 {CleanerEngine.HumanSize(totalBytes)}";
            }
        }
        catch (Exception ex)
        {
            RecycleSummaryText.Text = $"获取失败: {ex.Message}";
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadRecycleAsync();
    }

    private async void PurgeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "清空回收站 (永久清理)",
            Content = "确定要永久清空系统回收站中的全部项目吗？\n\n此操作将分批物理清理，彻底释放空间，且不会卡死系统。\n注意：此操作无法撤销！",
            PrimaryButtonText = "确认清空",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        RecycleSummaryText.Text = "正在极速物理清空回收站...";
        var (count, failed) = await Task.Run(() => RecycleManager.EmptySafely(new[] { @"C:\", @"D:\" }));

        var tip = new ContentDialog
        {
            Title = "清空完毕",
            Content = $"已永久移除 {count} 个项目。失败: {failed.Count} 项。\n系统回收站现已恢复纯净轻盈！",
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await tip.ShowAsync();

        await LoadRecycleAsync();
    }
}
