using System;
using System.IO;
using System.Runtime.InteropServices;
using DiskGuardian.UI.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIEx;

namespace DiskGuardian.UI;

public sealed partial class MainWindow : Window
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

    private const uint WM_SETICON = 0x0080;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private static readonly IntPtr ICON_SMALL = new(0);
    private static readonly IntPtr ICON_BIG = new(1);

    public MainWindow()
    {
        this.InitializeComponent();

        try
        {
            // 显式注册 AppUserModelID，确保 Windows 任务栏精准捕获独立程序身份与专属图标
            SetCurrentProcessExplicitAppUserModelID("HMuSeaB.DiskGuardian.UI");
        }
        catch { }

        try
        {
            // 窗口初始化：居中显示，设置尺寸与自定义标题栏
            this.CenterOnScreen();
            this.SetWindowSize(1200, 800);
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(AppTitleBar);

            SetupAppIcon();
        }
        catch { }

        // 默认进入仪表盘
        if (NavView.MenuItems.Count > 0)
        {
            NavView.SelectedItem = NavView.MenuItems[0];
        }
    }

    private void SetupAppIcon()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var icoCandidates = new[]
            {
                Path.Combine(baseDir, "disk-guardian.ico"),
                Path.Combine(baseDir, "Assets", "disk-guardian.ico"),
                Path.Combine(baseDir, "..", "disk-guardian.ico"),
                Path.Combine(AppContext.BaseDirectory, "disk-guardian.ico")
            };

            string? validIcoPath = null;
            foreach (var ico in icoCandidates)
            {
                if (File.Exists(ico))
                {
                    validIcoPath = ico;
                    break;
                }
            }

            if (validIcoPath != null)
            {
                // 1. WinUI 3 AppWindow 层面设置图标
                try
                {
                    this.SetIcon(validIcoPath);
                }
                catch { }

                // 2. Win32 原生 HWND 层面注入 WM_SETICON（彻底解决任务栏白色占位方块）
                try
                {
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                    if (hwnd != IntPtr.Zero)
                    {
                        IntPtr hBigIcon = LoadImage(IntPtr.Zero, validIcoPath, IMAGE_ICON, 32, 32, LR_LOADFROMFILE);
                        IntPtr hSmallIcon = LoadImage(IntPtr.Zero, validIcoPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);

                        if (hBigIcon != IntPtr.Zero)
                        {
                            SendMessage(hwnd, WM_SETICON, ICON_BIG, hBigIcon);
                        }
                        if (hSmallIcon != IntPtr.Zero)
                        {
                            SendMessage(hwnd, WM_SETICON, ICON_SMALL, hSmallIcon);
                        }
                    }
                }
                catch { }
            }
        }
        catch { }
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
