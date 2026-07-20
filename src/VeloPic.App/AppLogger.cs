namespace VeloPic.App;

internal static class AppLogger
{
    private static readonly object SyncRoot = new();

    public static string LogPath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VeloPic",
                "logs");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "startup.log");
        }
    }

    public static void Info(string message)
    {
        Write("INFO", message);
    }

    public static void Error(string message, Exception exception)
    {
        Write("ERROR", message + Environment.NewLine + exception);
    }

    private static void Write(string level, string message)
    {
        lock (SyncRoot)
        {
            try
            {
                var logPath = LogPath;
                if (File.Exists(logPath) && new FileInfo(logPath).Length > 2 * 1024 * 1024)
                {
                    File.Move(logPath, Path.ChangeExtension(logPath, ".previous.log"), overwrite: true);
                }

                File.AppendAllText(logPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {level} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // 日志不可写不应阻止应用启动或退出。
            }
            catch (UnauthorizedAccessException)
            {
                // 日志不可写不应阻止应用启动或退出。
            }
        }
    }
}
