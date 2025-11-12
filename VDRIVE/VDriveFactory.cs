using VDRIVE_Contracts.Interfaces;

namespace VDRIVE
{
    public class VDriveFactory
    {
        public static IVDriveServer CreateVDriveServer(string serverType, IConfiguration configuration, ILogger logger)
        {
            switch (serverType)
            {
                case "Tcp":
                    return new TcpVDriveServer(configuration, logger);
                case "Http":
                    return new HttpVDriveServer(configuration, logger);
                default:
                    throw new ArgumentException($"Unknown vdrive server type: {serverType}");
            }
        }

        public static IVDriveClient CreateVDriveClient(string clientType, IConfiguration configuration, ILogger logger)
        {
            switch (clientType)
            {
                case "Tcp":
                    return new TcpVDriveClient(configuration, logger);              
                default:
                    throw new ArgumentException($"Unknown vdrive client type: {clientType}");
            }
        }
    }
}
