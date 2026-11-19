using System.IO;

namespace WinMirroOverlay.Common;

/// <summary>极简文件日志：%LOCALAPPDATA%\Win-Mirro-Overlay\app.log。</summary>
internal static class AppLog
{
    private static readonly object Sync = new();

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Win-Mirro-Overlay");

    public static string FilePath { get; } = Path.Combine(Folder, "app.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? exception = null)
    {
        if (exception is null)
        {
            Write("ERROR", message);
            return;
        }

        Write("ERROR", $"{message} | {exception.GetType().Name} hr=0x{exception.HResult:X8} {exception.Message}");
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Folder);

                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > 4 * 1024 * 1024)
                {
                    info.Delete();
                }

                File.AppendAllText(
                    FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.ProcessId}] {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不能影响主流程。
        }
    }
}
