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
            var items = await Task.Run(() => RecycleManager.EnumerateRecycleBin(new[] { @"C:\", @"D:\" }));
            RecycleListView.ItemsSource = items;

            long totalBytes = items.Sum(i => i.SizeBytes);
            RecycleSummaryText.Text = $"{items.Count} 个对象，共计 {CleanerEngine.HumanSize(totalBytes)}";
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
            Title = "清空回收站",
            Content = "确定要永久清空回收站中的所有项目吗？此操作无法撤销！",
            PrimaryButtonText = "确认清空",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        var (count, failed) = await Task.Run(() => RecycleManager.EmptySafely(new[] { @"C:\", @"D:\" }));

        var tip = new ContentDialog
        {
            Title = "清空完毕",
            Content = $"已永久移除 {count} 个项目。失败: {failed.Count} 项。",
            CloseButtonText = "确定",
            XamlRoot = this.XamlRoot
        };
        await tip.ShowAsync();

        await LoadRecycleAsync();
    }
}
