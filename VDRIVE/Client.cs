using System.Net.Sockets;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE
{
    public class Client : VDriveBase, IClient
    {
        public Client(string d64Path)
        {
            this.ImagePath = d64Path;
        }
        public void Start()
        {
            if (!File.Exists(ImagePath))
            {
                throw new Exception("D64 path bad Ryan!");
            }

            int port = 80;
            using (TcpClient tcpClient = new TcpClient("192.168.1.38", port))
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

