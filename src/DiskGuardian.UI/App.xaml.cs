using System;
using System.IO;
using Microsoft.UI.Xaml;

namespace DiskGuardian.UI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        this.InitializeComponent();
        this.UnhandledException += (s, e) =>
        {
            RecordCrash("App.UnhandledException", e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            RecordCrash("CurrentDomain.UnhandledException", e.ExceptionObject as Exception);
        };
    }

    internal static void RecordCrash(string source, Exception? ex)
    {
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui_crash.log");
            File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex}\n");
        }
        catch { }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception ex)
        {
            RecordCrash("App.OnLaunched", ex);
            throw;
        }
    }
}
