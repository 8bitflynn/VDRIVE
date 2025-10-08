namespace VDRIVE_Contracts.Interfaces
{
    public interface IFloppyResolver
    {
        IList<string> SearchFloppys(string searchPattern);
        string InsertFloppyById(string id);
        string InsertFloppyByPath(string path);
        string GetInsertedFloppyPath();
        void EjectFloppy();
    }
}
