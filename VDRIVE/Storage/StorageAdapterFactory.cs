using VDRIVE.Drive.Impl;
using VDRIVE.Floppy.Impl;
using VDRIVE.Storage.Impl;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE.Drive
{
    public class StorageAdapterFactory
    {
        public static IStorageAdapter CreateStorageAdapter(string storageAdapterType, IConfiguration configuration, ILogger logger)
        {
            switch (storageAdapterType)
            {
                case "Vice":
                    return new ViceStorageAdapter(configuration, logger);
                case "DirMaster":
                    return new DirMasterStorageAdapter(configuration, logger);
                default:
                    throw new ArgumentException($"Unknown vdrive type: {storageAdapterType}");
            }
        }
    }   
}
