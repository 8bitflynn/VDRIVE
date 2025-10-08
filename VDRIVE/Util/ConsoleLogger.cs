using VDRIVE_Contracts.Interfaces;

namespace VDRIVE.Util
{
    public class ConsoleLogger : ILog
    {
        public void LogMessage(string message)
        {
            Console.WriteLine(message);
        }
    }
}
