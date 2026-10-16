using System.IO;
using System.Text;

namespace PSL.Core;

/// <summary>极简文件日志。</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static string? _file;

    /// <summary>当天日志文件路径。</summary>
    public static string CurrentFile
    {
        get { EnsureFile(); return _file!; }
    }

    private static void EnsureFile()
    {
        if (_file != null) return;
        lock (Gate)
        {
            if (_file != null) return;
            Directory.CreateDirectory(Paths.LogDir);
            _file = Path.Combine(Paths.LogDir, $"PSL-{DateTime.Now:yyyy-MM-dd}.log");
        }
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            EnsureFile();
            string line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {message}";
            lock (Gate) File.AppendAllText(_file!, line + Environment.NewLine, Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine(line);
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }
}
