using VDRIVE_Contracts.Structures;

namespace VDRIVE_Contracts.Interfaces
{
    public interface IStorageAdapter
    {
        LoadResponse Load(LoadRequest loadRequest, IFloppyResolver floppyResolver, out byte[] payload);
        SaveResponse Save(SaveRequest saveRequest, IFloppyResolver floppyResolver, byte[] payload);        
    }
}
