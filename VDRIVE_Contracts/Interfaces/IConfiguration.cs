namespace VDRIVE_Contracts.Interfaces
{
    public interface IConfiguration
    {
        string C1541Path { get; set; }
        List<string> SearchPaths { get; set; }
        string TempPath { get; set; } // path to use for temp files or leave empty to use system temp
        ushort ChunkSize { get; set; } // when sending data to C64, use this chunk size in bytes
        ushort MaxSearchResults { get; set; }
    }
}