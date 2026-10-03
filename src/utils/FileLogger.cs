using System.IO;

namespace LiveCaptionsTranslator.utils
{
    /// <summary>
    /// Minimal best-effort file logger for background diagnosis. Never throws.
    /// Files are written to ./logs and pruned after 7 days.
    /// </summary>
    public static class FileLogger
    {
        private const int RETAIN_DAYS = 7;

        private static readonly object _lock = new();
        private static readonly string logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
        private static DateTime lastPruneDate = DateTime.MinValue;

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);

        public static void Error(string message, Exception? ex = null) =>
            Write("ERROR", ex == null ? message : $"{message} {ex.GetType().Name}: {ex.Message}");

        private static void Write(string level, string message)
        {
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(logDir);
                    if (lastPruneDate != DateTime.Today)
                    {
                        lastPruneDate = DateTime.Today;
                        PruneOldLogs();
                    }

                    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(Path.Combine(logDir, $"{DateTime.Now:yyyyMMdd}.log"), line);
                }
            }
            catch
            {
                // Logging must never break the app.
            }
        }

        private static void PruneOldLogs()
        {
            try
            {
                var cutoff = DateTime.Now.AddDays(-RETAIN_DAYS);
                foreach (string file in Directory.GetFiles(logDir, "*.log"))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                        File.Delete(file);
                }
            }
            catch
            {
            }
        }
    }
}
