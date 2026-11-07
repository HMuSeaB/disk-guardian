using System;
using System.IO;
using System.Text.Json;

namespace DiskGuardian.Core.Engine;

/// <summary>
/// 审计日志器：每一行日志均立即 Flush，并在 out/actions.jsonl 中记录结构化动作流水。
/// </summary>
public sealed class AuditLogger : IDisposable
{
    private readonly StreamWriter? _writer;
    private readonly object _lock = new();

    public string LogFilePath { get; }

    public AuditLogger(string logFilePath)
    {
        LogFilePath = logFilePath;
        var dir = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _writer = new StreamWriter(new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true
        };
    }

    public void Log(string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
        lock (_lock)
        {
            _writer?.WriteLine(line);
            Console.WriteLine(line);
        }
    }

    public static void LogAction(string outDir, object actionData)
    {
        try
        {
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
            var actionFile = Path.Combine(outDir, "actions.jsonl");

            var payload = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                data = actionData
            };

            var json = JsonSerializer.Serialize(payload);
            File.AppendAllLines(actionFile, new[] { json });
        }
        catch { }
    }

    public void Dispose()
    {
        _writer?.Dispose();
    }
}
