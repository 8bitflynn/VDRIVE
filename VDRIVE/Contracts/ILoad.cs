using VDRIVE_Contracts.Structures;

namespace VDRIVE.Contracts
{
    public interface ILoad
    {
        LoadResponse Load(LoadRequest loadRequest, string imagePath, out byte[] payload);
    }
}
