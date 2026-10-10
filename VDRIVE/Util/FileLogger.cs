using VDRIVE_Contracts.Enums;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE.Util
{
    // Writes log lines to <folder>\vdrive-yyyyMMdd.log (one file a day), so the log can be read after the fact.
    // A failed write never stops the server.
    public class FileLogger : ILogger
    {
        private static readonly object FileLock = new object();
        private readonly LogSeverity LoggingSeverity;
        private readonly string LogFolder;

        public FileLogger(LogSeverity logSeverity) : this(logSeverity, Path.Combine(AppContext.BaseDirectory, "logs"))
        {
        }

        public FileLogger(LogSeverity logSeverity, string logFolder)
        {
            this.LoggingSeverity = logSeverity;
            this.LogFolder = logFolder;
        }

        public void LogMessage(string message, LogSeverity logSeverity = LogSeverity.Info)
        {
            if (logSeverity < this.LoggingSeverity)
            {
                return;
            }

            string line = $"[{DateTime.Now:HH:mm:ss.fff}] T#{Thread.CurrentThread.ManagedThreadId} {logSeverity}: {message}";
            try
            {
                lock (FileLock)
                {
                    Directory.CreateDirectory(this.LogFolder);
                    File.AppendAllText(Path.Combine(this.LogFolder, $"vdrive-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine);
                }
            }
            catch
            {
            }
        }
    }
}
