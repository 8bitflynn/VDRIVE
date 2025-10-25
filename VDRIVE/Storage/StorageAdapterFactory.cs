using VDRIVE.Drive.Impl;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE.Drive
{
    public class StorageAdapterFactory
    {
        public static IStorageAdapter CreateStorageAdapter(string vdriveStorageType, IConfiguration configuration, ILogger logger)
        {
            switch (vdriveStorageType)
            {
                case "Vice24":
                    return new Vice24VStorageAdapter(configuration, logger);                 
                case "DirMaster":
                    return new DirMasterStorageAdapter(configuration, logger);
                default:
                    throw new ArgumentException($"Unknown vdrive type: {vdriveStorageType}");
            }
        }
    }
}
