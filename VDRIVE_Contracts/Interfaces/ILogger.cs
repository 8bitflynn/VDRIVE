using VDRIVE_Contracts.Enums;

namespace VDRIVE_Contracts.Interfaces
{
    public interface ILogger
    {
        void LogMessage(string message, LogSeverity logSeverity = LogSeverity.Info); 
    }
}
