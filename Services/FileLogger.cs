using System;
using System.IO;

namespace backend.Services
{
    public static class FileLogger
    {
        private static readonly string LogFilePath = Path.Combine(AppContext.BaseDirectory, "logs", "error.log");
        private static readonly object LockObject = new object();

        public static void LogError(string message, string source, string? stackTrace = null)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogFilePath);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                lock (LockObject)
                {
                    using var writer = new StreamWriter(LogFilePath, true);
                    writer.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] [{source}] ERROR: {message}");
                    if (!string.IsNullOrEmpty(stackTrace))
                    {
                        writer.WriteLine($"Stack Trace:\n{stackTrace}");
                    }
                    writer.WriteLine(new string('-', 60));
                }
            }
            catch
            {
                // Bỏ qua nếu có lỗi phát sinh trong khi ghi log để tránh vòng lặp crash
            }
        }
    }
}
