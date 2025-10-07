using VDRIVE_Contracts.Structures;

namespace VDRIVE_Contracts.Interfaces
{
    public interface ILoad
    {
        LoadResponse Load(LoadRequest loadRequest, string imagePath, out byte[] payload);
    }
}
