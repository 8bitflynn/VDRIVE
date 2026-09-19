namespace VDRIVE_Contracts.Structures
{
    public class StorageAdapterSettings
    {
        public bool Readonly { get; set; }
        public string NewFloppyPath { get; set; }
        public DirMasterSettings DirMaster { get; set; }
        public ViceSettings Vice { get; set; }
        public int LockTimeoutSeconds { get; set; }
    }
}
