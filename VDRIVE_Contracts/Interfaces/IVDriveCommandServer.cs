namespace VDRIVE_Contracts.Interfaces
{
    public interface IVDriveCommandServer
    {
        void Mount(string sessionId, string imageId);          // no return
        IEnumerable<string> Search(string sessionId, string term); // yields until '\0'
        byte[] Load(string sessionId, string fileName);        // returns file contents
        void Save(string sessionId, string fileName, byte[] data); // no return
    }
}