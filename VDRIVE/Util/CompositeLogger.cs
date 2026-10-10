using VDRIVE_Contracts.Enums;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE.Util
{
    // Sends each message to several loggers (console and file)
    public class CompositeLogger : ILogger
    {
        private readonly ILogger[] Loggers;

        public CompositeLogger(params ILogger[] loggers)
        {
            this.Loggers = loggers;
        }

        public void LogMessage(string message, LogSeverity logSeverity = LogSeverity.Info)
        {
            foreach (ILogger logger in this.Loggers)
            {
                logger.LogMessage(message, logSeverity);
            }
        }
    }
}
