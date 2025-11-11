using VDRIVE_Contracts.Interfaces;

namespace VDRIVE_Contracts.Structures
{
    public class Session
    {
        public IFloppyResolver FloppyResolver { get; set; }
        public IStorageAdapter StorageAdapter { get; set; }
        public IProcessRunner ProcessRunner { get; set; }
        public ClientInfo ClientInfo { get; set; }
    }
}
