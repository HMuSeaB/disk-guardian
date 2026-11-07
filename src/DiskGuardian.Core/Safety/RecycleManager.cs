using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace DiskGuardian.Core.Safety;

/// <summary>
/// Windows 原生安全回收站管理器：
/// 1. 两步删除之步骤一：移入回收站 (FOF_ALLOWUNDO)；
/// 2. 严禁调用 SHEmptyRecycleBinW，避免损坏文件 (如 $R02DUPK / 0x80004005) 导致系统模态框卡死；
/// 3. 安全扫描 $I/$R 分批物理清理，坏项跳过。
/// </summary>
public static class RecycleManager
{
    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;

    private const ushort FlagsTrash = FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_ALLOWUNDO;
    private const ushort FlagsPermanent = FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW lpFileOp);

    private static (int Rc, bool Aborted) ExecuteFileOp(IReadOnlyList<string> paths, ushort flags)
    {
        if (paths.Count == 0) return (0, false);

        // SHFileOperation 要求以双 null 结尾的宽字符缓冲
        var multiString = string.Join("\0", paths) + "\0\0";

        var fileOp = new SHFILEOPSTRUCTW
        {
            hwnd = IntPtr.Zero,
            wFunc = FO_DELETE,
            pFrom = multiString,
            pTo = null,
            fFlags = flags,
            fAnyOperationsAborted = false,
            hNameMappings = IntPtr.Zero,
            lpszProgressTitle = null
        };

        var rc = SHFileOperationW(ref fileOp);
        return (rc, fileOp.fAnyOperationsAborted);
    }

    /// <summary>
    /// 将目标移入回收站（可撤销）
    /// </summary>
    public static (int ReturnCode, bool StillExists) SendToRecycleBin(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var (rc, _) = ExecuteFileOp(new[] { fullPath }, FlagsTrash);
        var stillExists = File.Exists(fullPath) || Directory.Exists(fullPath);
        return (rc, stillExists);
    }

    /// <summary>
    /// 批量安全移入回收站或永久删除（失败时分拆重试，不弹模态框）
    /// </summary>
    public static (List<string> Succeeded, List<(string Path, int ErrorCode)> Failed) SendMany(
        IReadOnlyList<string> paths,
        int batchSize = 40,
        bool permanent = false)
    {
        var flags = permanent ? FlagsPermanent : FlagsTrash;
        var succeeded = new List<string>();
        var failed = new List<(string, int)>();

        for (int i = 0; i < paths.Count; i += batchSize)
        {
            var chunk = new List<string>();
            for (int j = i; j < Math.Min(i + batchSize, paths.Count); j++)
            {
                chunk.Add(Path.GetFullPath(paths[j]));
            }

            var (rc, _) = ExecuteFileOp(chunk, flags);
            if (rc == 0)
            {
                succeeded.AddRange(chunk);
                continue;
            }

            // 批次失败时，退回单项尝试，避免单项损坏卡住整体
            foreach (var item in chunk)
            {
                var (singleRc, _) = ExecuteFileOp(new[] { item }, flags);
                if (singleRc == 0)
                {
                    succeeded.Add(item);
                }
                else
                {
                    failed.Add((item, singleRc));
                }
            }
        }

        return (succeeded, failed);
    }

    public sealed record RecycleItem(long SizeBytes, string OriginalPath, string DeletedFileName, DateTime DeletedTime);

    /// <summary>
    /// 枚举回收站内的物理文件（解析 $Recycle.Bin 下的 $I 和 $R）
    /// </summary>
    public static List<RecycleItem> EnumerateRecycleBin(IEnumerable<string>? driveRoots = null)
    {
        var drives = driveRoots ?? new[] { @"C:\" };
        var results = new List<RecycleItem>();

        foreach (var drive in drives)
        {
            var binPath = Path.Combine(drive, "$Recycle.Bin");
            if (!Directory.Exists(binPath)) continue;

            try
            {
                var sidDirs = Directory.GetDirectories(binPath);
                foreach (var sidDir in sidDirs)
                {
                    try
                    {
                        var iFiles = Directory.GetFiles(sidDir, "$I*");
                        foreach (var iFile in iFiles)
                        {
                            var rFile = Path.Combine(sidDir, "$R" + Path.GetFileName(iFile).Substring(2));
                            long size = 0;
                            if (File.Exists(rFile))
                            {
                                try { size = new FileInfo(rFile).Length; } catch { }
                            }
                            else if (Directory.Exists(rFile))
                            {
                                // 目录大小
                                try { size = GetDirectorySize(rFile); } catch { }
                            }

                            results.Add(new RecycleItem(size, rFile, Path.GetFileName(iFile), File.GetCreationTime(iFile)));
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        return results;
    }

    /// <summary>
    /// 安全分批物理清空回收站，不调用容易死锁的 SHEmptyRecycleBinW
    /// </summary>
    public static (int SucceededCount, List<string> FailedPaths) EmptySafely(IEnumerable<string>? driveRoots = null)
    {
        var items = EnumerateRecycleBin(driveRoots);
        var targetPaths = new List<string>();

        foreach (var item in items)
        {
            targetPaths.Add(item.OriginalPath);
        }

        var (succeeded, failed) = SendMany(targetPaths, batchSize: 50, permanent: true);
        var failedList = new List<string>();
        foreach (var f in failed) failedList.Add(f.Path);

        return (succeeded.Count, failedList);
    }

    private static long GetDirectorySize(string path)
    {
        long total = 0;
        try
        {
            var files = Directory.GetFiles(path, "*", SearchOption.AllDirectories);
            foreach (var f in files)
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
        }
        catch { }
        return total;
    }
}
