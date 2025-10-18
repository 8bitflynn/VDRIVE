using VDRIVE_Contracts.Interfaces;

namespace VDRIVE_Contracts.Structures
{
    public class Configuration : IConfiguration
    {
        public string FloppyResolver { get; set; }
        public string C1541Path { get; set; }
        public List<string> SearchPaths { get; set; } = new List<string>();
        public string MediaExtensionAllowed { get; set; }
        public string TempPath { get; set; }
        public string TempFolder { get; set; }
        public ushort ChunkSize { get; set; }
        public ushort MaxSearchResults { get; set; }        
        public string ServerListenAddress { get; set; }
        public int? ServerPort { get; set; }
        public string ClientAddress { get; set; }
        public int? ClientPort { get; set; }
    }
}
