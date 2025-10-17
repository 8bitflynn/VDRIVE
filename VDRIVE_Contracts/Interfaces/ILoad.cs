using VDRIVE_Contracts.Structures;

namespace VDRIVE_Contracts.Interfaces
{
    public interface ILoad
    {
        LoadResponse Load(LoadRequest loadRequest, IFloppyResolver floppyResolver, out byte[] payload);
        string BuildImage(string outputPathToImage, string imageType);
        bool AddFileToImage(string imagePath, string filePathInImage, byte[] fileData, bool overwrite);
    }
}
