namespace VDRIVE_Contracts.Structures
{
    public class StorageAdapterSettings
    {
        public bool Readonly { get; set; }
        public string NewFloppyPath { get; set; }
        public string GameSavePath { get; set; }    // optional: SAVE"SAV-<id>" from a game goes here as its own file (GameSave)
        public DirMasterSettings DirMaster { get; set; }
        public ViceSettings Vice { get; set; }
        public int LockTimeoutSeconds { get; set; }
    }
}
