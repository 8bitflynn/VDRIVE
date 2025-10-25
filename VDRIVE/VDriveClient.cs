using System.Net.Sockets;
using VDRIVE.Drive;
using VDRIVE.Floppy;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE
{
    public class VDriveClient : VDriveBase, IVDriveClient
    {
        public VDriveClient(string ipAddress, int port, IConfiguration configuration, ILogger logger)
        {
            this.Configuration = configuration;
            this.Logger = logger;
            this.IPAddress = ipAddress;
            this.Port = port;
        }
        private readonly string IPAddress;
        private readonly int Port;

        public void Start()
        {
            if (string.IsNullOrEmpty(this.IPAddress))
            {
                throw new Exception("Invalid IP address!");
            }
            
            using (TcpClient tcpClient = new TcpClient(this.IPAddress, this.Port))
            {
                tcpClient.NoDelay = true;
                NetworkStream networkStream = tcpClient.GetStream();

                // instance dependencies
                IFloppyResolver floppyResolver = FloppyResolverFactory.CreateFloppyResolver(this.Configuration.FloppyResolver, this.Configuration, this.Logger);
                IStorageAdapter storageAdapter = StorageAdapterFactory.CreateStorageAdapter(this.Configuration.StorageAdapter, this.Configuration, this.Logger);

                while (tcpClient.Connected)
                {
                    this.HandleClient(tcpClient, networkStream, floppyResolver, storageAdapter);
                }
            }
        }
    }
}

