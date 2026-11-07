using System;
using System.IO;
using Microsoft.UI.Xaml.Controls;

namespace DiskGuardian.UI.Views;

public partial class DashboardPage : Page
{
    public DashboardPage()
    {
        this.InitializeComponent();
        RefreshDrives();
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
}
