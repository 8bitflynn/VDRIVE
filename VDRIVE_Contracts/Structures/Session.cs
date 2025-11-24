using System.Diagnostics;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE_Contracts.Structures
{
    [DebuggerDisplay("SessionId={ClientInfo.SessionId} ConnectedAt: {ClientInfo.ConnectedAt} LastAccess: {ClientInfo.LastAccess}")]
    public class Session
    {
        public ushort SessionId { get; set; }
        public IFloppyResolver FloppyResolver { get; set; }
        public IStorageAdapter StorageAdapter { get; set; }
        public IProcessRunner ProcessRunner { get; set; }
        public ClientInfo ClientInfo { get; set; } = new ClientInfo();
        
        // Search result caching for pagination
        public FloppyInfo[] CachedSearchResults { get; set; }
        public string LastSearchTerm { get; set; }
        public int CurrentSearchPage { get; set; }
    }
}
