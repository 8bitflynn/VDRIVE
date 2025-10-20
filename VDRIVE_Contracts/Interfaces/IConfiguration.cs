namespace VDRIVE_Contracts.Interfaces
{
    public interface IConfiguration
    {
        string FloppyResolver { get; set; } // type of floppy resolver to use: Local, CommodoreSoftware, C64, etc...
        string C1541Path { get; set; } // Vice 2.4 c1541 executable path
        List<string> SearchPaths { get; set; } // paths to search for media files when using LocalFloppyResolver
        string MediaExtensionAllowed { get; set; } // csv of allowed media extension, e.g. ".d64" or ".g64" - c64 can override
        string TempPath { get; set; } // path to use for temp files or leave empty to use system temp
        string TempFolder { get; set; } // folder name inside TempPath to use for temp files
        ushort ChunkSize { get; set; } // when sending data to C64, use this chunk size in bytes
        ushort MaxSearchResults { get; set; } // max results to C64
        string ServerOrClientMode { get; set; } // "Server" or "Client" (ESP8266 firmware should be the opposite of this when connecting from c64)
        string ServerListenAddress { get; set; }
        int? ServerPort { get; set; }
        string ClientAddress { get; set; }
        int? ClientPort { get; set; }
        int? SendTimeoutSeconds { get; set; }
        int? ReceiveTimeoutSeconds { get; set; }
    }
}