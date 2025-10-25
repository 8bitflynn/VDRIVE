using VDRIVE_Contracts.Enums;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE.Util
{
    public class ConsoleLogger : ILogger
    {
        public void LogMessage(string message, LogSeverity logSeverity = LogSeverity.Info)
        {
            Console.WriteLine($"[{logSeverity}] {message} ");
        }
    }
}
