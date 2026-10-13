using VDRIVE_Contracts.Structures;
using VDRIVE_Contracts.Structures.Http;

namespace VDRIVE_Contracts.Interfaces
{
    public interface IStorageAdapter
    {
        LoadResponse Load(LoadRequest loadRequest, IFloppyResolver floppyResolver, out byte[] payload);
        SaveResponse Save(SaveRequest saveRequest, IFloppyResolver floppyResolver, byte[] payload);
        CreateFloppyResponse CreateFloppyImage(CreateFloppyRequest createDiskRequest);

        // the session's own folder for extracted files and dir.prg (VDRIVE_Scratch\sessions\<id>); null = one per thread
        string WorkFolder { get; set; }
    }
}
