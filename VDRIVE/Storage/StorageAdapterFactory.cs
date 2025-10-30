using VDRIVE.Drive.Impl;
using VDRIVE.Storage.Impl;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE.Drive
{
    public class StorageAdapterFactory
    {
        public static IStorageAdapter CreateStorageAdapter(string storageAdapterType, IProcessRunner processRunner, IConfiguration configuration, ILogger logger)
        {
            switch (storageAdapterType)
            {
                case "Vice":
                    return new ViceStorageAdapter(processRunner, configuration, logger);
                case "DirMaster":
                    return new DirMasterStorageAdapter(processRunner, configuration, logger);
                default:
                    throw new ArgumentException($"Unknown vdrive type: {storageAdapterType}");
            }
        }
    }   
}
