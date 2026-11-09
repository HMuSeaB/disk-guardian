using System;
using System.IO;

namespace DiskGuardian.Core.Safety;

public static class ShortcutManager
{
    /// <summary>
    /// 在用户当前桌面创建规范的 Windows 快捷方式 (DiskGuardian.lnk)
    /// </summary>
    public static (bool Success, string ShortcutPath, string? ErrorMessage) CreateDesktopShortcut()
    {
        try
        {
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktopDir) || !Directory.Exists(desktopDir))
            {
                desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            }

            var shortcutPath = Path.Combine(desktopDir, "DiskGuardian.lnk");

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var targetExe = Path.Combine(baseDir, "DiskGuardian.UI.exe");

            // 如果当前运行在 app/ 子目录，优先指向根目录启动器 DiskGuardian.exe
            var parentDir = Path.GetDirectoryName(baseDir.TrimEnd('\\', '/'));
            if (!string.IsNullOrEmpty(parentDir))
            {
                var rootExe = Path.Combine(parentDir, "DiskGuardian.exe");
                if (File.Exists(rootExe))
                {
                    targetExe = rootExe;
                    baseDir = parentDir;
                }
            }

            if (!File.Exists(targetExe))
            {
                targetExe = Environment.ProcessPath ?? targetExe;
            }

            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return (false, "", "系统中未找到 WScript.Shell 组件");
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(shortcutPath);
            shortcut.TargetPath = targetExe;
            shortcut.WorkingDirectory = Path.GetDirectoryName(targetExe);
            shortcut.WindowStyle = 1;
            shortcut.Description = "DiskGuardian - Windows 原生系统空间与网络守护者";
            var iconPath = Path.Combine(Path.GetDirectoryName(targetExe) ?? baseDir, "disk-guardian.ico");
            shortcut.IconLocation = File.Exists(iconPath) ? $"{iconPath}, 0" : $"{targetExe}, 0";
            shortcut.Save();

            return (true, shortcutPath, null);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }
}
