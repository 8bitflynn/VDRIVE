using VDRIVE_Contracts.Enums;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE.Util
{
    public class SessionLogger : ILogger
    {
        public SessionLogger(ushort sessionId) : this(sessionId, LogSeverity.Info)
        {
        }

        public SessionLogger(ushort sessionId,  LogSeverity logSeverity)
        {
            this.SessionId = sessionId;
            this.LoggingSeverity = logSeverity;
        }
        private readonly ushort SessionId;
        private LogSeverity LoggingSeverity = LogSeverity.Verbose;       

        public void LogMessage(string message, LogSeverity logSeverity = LogSeverity.Info)
        {
            if (logSeverity < this.LoggingSeverity)
            {
                return;
            }

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] S#{this.SessionId} {logSeverity}: {message}");
        }      
    }
}
