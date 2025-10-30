using VDRIVE_Contracts.Structures;

namespace VDRIVE_Contracts.Interfaces
{
    public interface IFloppyResolver
    {
        SearchFloppyResponse SearchFloppys(SearchFloppiesRequest searchFloppiesRequest, out FloppyInfo[] foundFloppyInfos);
        FloppyInfo InsertFloppy(FloppyInfo floppyInfo);
        FloppyInfo InsertFloppy(FloppyIdentifier floppyIdentifier);
        FloppyInfo GetInsertedFloppyInfo();
        FloppyPointer GetInsertedFloppyPointer();        
        FloppyInfo EjectFloppy();
    }
}
