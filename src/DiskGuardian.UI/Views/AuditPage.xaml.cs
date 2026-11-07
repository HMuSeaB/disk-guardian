using System;
using System.IO;
using System.Linq;
using Microsoft.UI.Xaml.Controls;

namespace DiskGuardian.UI.Views;

public partial class AuditPage : Page
{
    public AuditPage()
    {
        this.InitializeComponent();
        LoadLogs();
    }

    private void LoadLogs()
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var outDir = Path.Combine(baseDir, "out");
            while (!Directory.Exists(outDir) && !string.IsNullOrEmpty(baseDir))
            {
                baseDir = Path.GetDirectoryName(baseDir);
                if (string.IsNullOrEmpty(baseDir)) break;
                outDir = Path.Combine(baseDir, "out");
            }

            if (!Directory.Exists(outDir))
            {
                LogTextBox.Text = "暂无日志文件 (out/ 目录尚未生成)。";
                return;
            }

            var logFiles = Directory.GetFiles(outDir, "*.log").OrderByDescending(File.GetLastWriteTime).ToList();
            if (logFiles.Count == 0)
            {
                LogTextBox.Text = "暂无日志文件。";
                return;
            }

            var newest = logFiles.First();
            var content = File.ReadAllText(newest);
            LogTextBox.Text = $"=== 最新日志: {Path.GetFileName(newest)} ===\n\n" + content;
        }
        catch (Exception ex)
        {
            LogTextBox.Text = $"读取日志失败: {ex.Message}";
        }
    }
}
