using VDRIVE_Contracts.Interfaces;

namespace VDRIVE_Contracts.Structures
{
    public class Configuration : IConfiguration
    {
        public string C1541Path { get; set; }
        public List<string> SearchPaths { get; set; } = new List<string>();     
        
        // TEMP until the C64 can request / search floppies
        public string ImageName { get; set; }
    }
}
