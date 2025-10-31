using VDRIVE_Contracts.Enums;

namespace VDRIVE_Contracts.Structures
{
    public class RunProcessParameters
    {
        public string ImagePath { get; set; }
        public string Arguments { get; set; }
        public string ExecutablePath { get; set; }     
        public LockType LockType { get; set; }
        public int LockTimeoutSeconds { get; set; }
    }
}
