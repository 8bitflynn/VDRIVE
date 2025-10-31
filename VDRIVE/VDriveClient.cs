using System.Net.Sockets;
using VDRIVE.Drive;
using VDRIVE.Floppy;
using VDRIVE.Util;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE
{
    public class VDriveClient : IVDriveClient
    {
        public VDriveClient(IConfiguration configuration, ILogger logger)
        {
            this.Configuration = configuration;
            this.Logger = logger;
            this.IPAddress = this.Configuration.ClientAddress;
            this.Port = this.Configuration.ClientPort.Value;
        }
        private readonly string IPAddress;
        private readonly int Port;
        protected IConfiguration Configuration;
        protected ILogger Logger;

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
                IProtocolHandler protocolHandler = new ProtocolHandler(this.Configuration, this.Logger);
                IProcessRunner processRunner = new LockingProcessRunner(this.Configuration, this.Logger);
                IFloppyResolver floppyResolver = FloppyResolverFactory.CreateFloppyResolver(this.Configuration.FloppyResolver, this.Configuration, this.Logger);
                IStorageAdapter storageAdapter = StorageAdapterFactory.CreateStorageAdapter(this.Configuration.StorageAdapter, processRunner, this.Configuration, this.Logger);

                while (tcpClient.Connected)
                {
                    protocolHandler.HandleClient(tcpClient, networkStream, floppyResolver, storageAdapter);
                }
            }
        }
    }
}

