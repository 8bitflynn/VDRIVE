using VDRIVE_Contracts.Structures;

namespace VDRIVE_Contracts.Interfaces
{
    public interface ISave
    {
        SaveResponse Save(SaveRequest saveRequest, string imagePath, byte[] payload);
    }
}
