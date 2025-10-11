using VDRIVE_Contracts.Structures;

namespace VDRIVE_Contracts.Interfaces
{
    public interface IFloppyResolver
    {
        SearchFloppyResponse SearchFloppys(SearchFloppiesRequest searchFloppiesRequest);
        FloppyInfo? InsertFloppy(FloppyInfo floppyInfo);
        FloppyInfo? GetInsertedFloppyInfo();
        FloppyPointer? GetInsertedFloppyPointer();
        FloppyInfo? EjectFloppy();
    }
}
