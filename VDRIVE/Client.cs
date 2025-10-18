using System.Net.Sockets;
using VDRIVE.Drive.Vice;
using VDRIVE.Floppy;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE
{
    public class Client : VDriveBase, IClient
    {
        public Client(string ipAddress, int port, IConfiguration configuration, ILog logger)
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

                //IFloppyResolver floppyResolver = new LocalFloppyResolver(this.Configuration, this.Logger);
                IFloppyResolver floppyResolver = new CommodoreSoftwareFloppyResolver(this.Configuration, this.Logger);

                ILoad loader = new ViceLoad(this.Configuration, this.Logger);
                ISave saver = new ViceSave(this.Configuration, this.Logger);

                while (tcpClient.Connected)
                {
                    this.HandleClient(tcpClient, networkStream, floppyResolver, loader, saver);
                }
            }
        }
    }
}

