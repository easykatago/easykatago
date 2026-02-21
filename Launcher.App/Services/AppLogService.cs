using System.IO;
using System.Text;

namespace Launcher.App.Services;

public static class AppLogService
{
    private static readonly Lock Sync = new();
    private static string _logsRoot = Path.Combine(AppContext.BaseDirectory, "data", "logs");

    public static void Initialize(string logsRoot)
    {
        _logsRoot = logsRoot;
        Directory.CreateDirectory(_logsRoot);
        Info("日志系统已初始化。");
    }

    public static void Info(string message) => Write("INF", message);
    public static void Warn(string message) => Write("WRN", message);
    public static void Error(string message) => Write("ERR", message);

    public static IReadOnlyList<string> GetLogFiles()
    {
        if (!Directory.Exists(_logsRoot))
        {
            return [];
        }

        return Directory
            .GetFiles(_logsRoot, "*.log", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }

    public static string ReadTail(string filePath, int maxLines = 200)
    {
        if (!File.Exists(filePath))
        {
            return string.Empty;
        }

        var lines = File.ReadLines(filePath).TakeLast(maxLines);
        return string.Join(Environment.NewLine, lines);
    }

    private static void Write(string level, string message)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(_logsRoot);
            var logPath = Path.Combine(_logsRoot, $"{DateTime.Now:yyyyMMdd}.log");
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
        }
    }
}
