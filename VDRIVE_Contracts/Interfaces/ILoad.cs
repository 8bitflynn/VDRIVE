using VDRIVE_Contracts.Structures;

namespace VDRIVE_Contracts.Interfaces
{
    public interface ILoad
    {
        LoadResponse Load(LoadRequest loadRequest, IFloppyResolver floppyResolver, out byte[] payload);       
    }
}
