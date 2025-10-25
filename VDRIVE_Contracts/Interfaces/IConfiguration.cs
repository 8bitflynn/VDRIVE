using VDRIVE_Contracts.Structures;

namespace VDRIVE_Contracts.Interfaces
{
    public interface IConfiguration
    {
        public string StorageAdapter { get; set; } 
        public string FloppyResolver { get; set; } 
        public ushort MaxSearchResults { get; set; }
        public string TempPath { get; set; }
        public string TempFolder { get; set; }
        public ushort ChunkSize { get; set; }
        public string ServerOrClientMode { get; set; }
        public string ServerListenAddress { get; set; }
        public int? ServerPort { get; set; }
        public string ClientAddress { get; set; }
        public int? ClientPort { get; set; }
        public int? SendTimeoutSeconds { get; set; }
        public int? ReceiveTimeoutSeconds { get; set; }
        public StorageAdapterSettings StorageAdapterSettings { get; set; }
        public FloppyResolverSettings FloppyResolverSettings { get; set; }
    }
}