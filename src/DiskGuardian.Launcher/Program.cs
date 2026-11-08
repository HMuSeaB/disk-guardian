using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace DiskGuardian.Launcher;

public static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;

    [STAThread]
    public static int Main(string[] args)
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var appExe = Path.Combine(baseDir, "app", "DiskGuardian.UI.exe");

        if (!File.Exists(appExe))
        {
            // fallback: 如果用户把 exe 拷到了同一个目录
            appExe = Path.Combine(baseDir, "DiskGuardian.UI.exe");
        }

        if (!File.Exists(appExe))
        {
            MessageBoxW(
                IntPtr.Zero,
                $"找不到应用程序核心组件:\n{appExe}\n\n请确认解压时完整保留了 app 目录及全部依赖文件。",
                "DiskGuardian 启动错误",
                MB_OK | MB_ICONERROR);
            return 1;
        }

        var psi = new ProcessStartInfo
        {
            FileName = appExe,
            WorkingDirectory = Path.GetDirectoryName(appExe)!,
            UseShellExecute = false
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        try
        {
            Process.Start(psi);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBoxW(
                IntPtr.Zero,
                $"启动 DiskGuardian 失败:\n{ex.Message}",
                "DiskGuardian 启动错误",
                MB_OK | MB_ICONERROR);
            return 1;
        }
    }
}
