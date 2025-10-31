namespace VDRIVE_Contracts.Structures
{
    public class StorageAdapterSettings
    {
        public DirMasterSettings DirMaster { get; set; }
        public ViceSettings Vice { get; set; }
        public int LockTimeoutSeconds { get; set; }
    }
}
