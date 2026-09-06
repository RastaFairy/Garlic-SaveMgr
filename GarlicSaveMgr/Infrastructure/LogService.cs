using System.Text;

namespace GarlicSaveMgr.Infrastructure;

public static class LogService
{
    private static readonly object Sync = new();
    private static string _file = "";

    public static event Action<string, string>? Message;

    public static void Initialize()
    {
        AppPaths.EnsureDirectories();
        _file = Path.Combine(AppPaths.LogsDirectory, $"{DateTime.Now:yyyyMMdd_HHmmss}.log");
    }

    public static void Write(string text, string level = "INFO")
    {
        var line = $"{DateTime.Now:O} {level} {text}";
        lock (Sync)
        {
            try { File.AppendAllText(_file, line + Environment.NewLine, Encoding.UTF8); }
            catch { /* logging must never crash the app */ }
        }
        Message?.Invoke(text, level.ToLowerInvariant());
    }

    public static string CurrentLogFile => _file;
}
