using System;
using System.IO;

namespace Guardian.Core.Services
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        private static readonly string _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Guardian",
            "Logs"
        );
        private static readonly string _logFile = Path.Combine(_logDirectory, "guardian.log");

        static Logger()
        {
            try
            {
                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }
            }
            catch { }
        }

        public static void Info(string message) => Log("INFO", message);
        public static void Warn(string message) => Log("WARN", message);
        public static void Error(string message, Exception? ex = null)
        {
            string fullMsg = ex == null ? message : $"{message} | Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}";
            Log("ERROR", fullMsg);
        }

        private static void Log(string level, string message)
        {
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
            try
            {
                lock (_lock)
                {
                    File.AppendAllText(_logFile, line + Environment.NewLine);
                    Console.WriteLine(line);
                }
            }
            catch { }
        }
    }
}
