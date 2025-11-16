using VDRIVE_Contracts.Enums;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE.Util
{
    public class ConsoleLogger : ILogger
    {
        public ConsoleLogger() : this(LogSeverity.Info)
        {            
        }

        public ConsoleLogger(LogSeverity logSeverity)
        {
            this.LoggingSeverity = logSeverity;            
        }
        private LogSeverity LoggingSeverity = LogSeverity.Verbose;

        public void LogMessage(string message, LogSeverity logSeverity = LogSeverity.Info)
        {
            if (logSeverity < this.LoggingSeverity)
            {
                return;
            }   

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] T#{Thread.CurrentThread.ManagedThreadId} {logSeverity}: {message}");
        }
    }
}
