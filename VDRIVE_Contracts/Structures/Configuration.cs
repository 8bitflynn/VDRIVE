using VDRIVE_Contracts.Interfaces;

namespace VDRIVE_Contracts.Structures
{
    public class Configuration : IConfiguration
    {
        public IList<string> AllowedAuthTokens { get; set; } = new List<string>();
        public string StorageAdapter { get; set; }
        public string FloppyResolver { get; set; }
        public string LoggingLevel { get; set; }
        public int? SessionTimeoutMinutes { get; set; }
        public string ServerType { get; set; }
        public ushort MaxSearchResults { get; set; }
        public int SearchPageSize { get; set; }
        public string SearchIntroMessage { get; set; }
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
