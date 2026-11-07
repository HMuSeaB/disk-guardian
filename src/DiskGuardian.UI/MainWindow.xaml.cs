using System;
using DiskGuardian.UI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIEx;

namespace DiskGuardian.UI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();

        try
        {
            // 窗口初始化：居中显示，设置尺寸与自定义标题栏
            this.CenterOnScreen();
            this.SetWindowSize(1200, 800);
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);
        }
        catch { }

        // 默认进入仪表盘
        if (NavView.MenuItems.Count > 0)
        {
            NavView.SelectedItem = NavView.MenuItems[0];
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            Type pageType = tag switch
            {
                "dashboard" => typeof(DashboardPage),
                "cleaner" => typeof(CleanerPage),
                "recycle" => typeof(RecyclePage),
                "audit" => typeof(AuditPage),
                _ => typeof(DashboardPage)
            };

            ContentFrame.Navigate(pageType);
        }
    }
}
