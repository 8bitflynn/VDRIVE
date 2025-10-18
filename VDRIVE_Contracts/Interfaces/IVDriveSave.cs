using VDRIVE_Contracts.Structures;

namespace VDRIVE_Contracts.Interfaces
{
    public interface IVDriveSave
    {
        SaveResponse Save(SaveRequest saveRequest, IFloppyResolver floppyResolver, byte[] payload);
    }
}
