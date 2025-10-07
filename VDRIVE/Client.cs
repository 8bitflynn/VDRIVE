using System.Net.Sockets;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE
{
    public class Client : VDriveBase, IClient
    {
        public Client(string imagePath, string ipAddress, int port)
        {
            this.ImagePath = imagePath;
            this.IPAddress = ipAddress;
            this.Port = port;
        }
        private readonly string IPAddress;
        private readonly int Port;

        public void Start()
        {
            if (!File.Exists(this.ImagePath))
            {
                throw new Exception("Invalid image path!");
            }

            if (string.IsNullOrEmpty(this.IPAddress))
            {
                throw new Exception("Invalid IP address!");
            }

            
            using (TcpClient tcpClient = new TcpClient(this.IPAddress, this.Port))
            {
                tcpClient.NoDelay = true;
                NetworkStream networkStream = tcpClient.GetStream();

                while (tcpClient.Connected)
                {
                    this.HandleClient(tcpClient, networkStream);
                }
            }
        }
    }
}

