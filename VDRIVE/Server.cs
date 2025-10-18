using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using VDRIVE.Drive.Vice;
using VDRIVE.Floppy;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE
{
    public class Server : VDriveBase, IServer
    {
        public Server(IConfiguration configuation, ILog logger, string listenIp = null, int port = 6510)
        {
            this.Configuration = configuation;
            this.Logger = logger;

            this.Port = port;
            if (!string.IsNullOrWhiteSpace(listenIp))
            {
                this.ListenAddress = IPAddress.Parse(listenIp);
            }
            else
            {
                this.ListenAddress = GetLocalIPv4Address() ?? IPAddress.Any;
            }
        }
        private readonly IPAddress ListenAddress;
        private readonly int Port;

        public void Start()
        {
            TcpListener listener = new TcpListener(this.ListenAddress, Port);
            listener.Start();

            this.Logger.LogMessage($"Listening on {this.ListenAddress}:{Port}");

            while (true)
            {
                TcpClient client = listener.AcceptTcpClient(); // blocking
                client.NoDelay = true;              

                Task.Run(() =>
                {
                    // for now change in code which floppy resovler to use...
                    IFloppyResolver floppyResolver = new LocalFloppyResolver(this.Configuration, this.Logger);
                    //IFloppyResolver floppyResolver = new CommodoreSoftwareFloppyResolver(this.Configuration, this.Logger);

                    ILoad loader = new ViceLoad(this.Configuration, this.Logger);
                    ISave saver = new ViceSave(this.Configuration, this.Logger);

                    this.HandleClient(client, floppyResolver, loader, saver);

                });
            }
        }

        private void HandleClient(TcpClient tcpClient, IFloppyResolver floppyResolver, ILoad loader, ISave saver)
        {
            string ip = tcpClient.Client.RemoteEndPoint.ToString();

            this.Logger.LogMessage($"Client connected: {ip}");

            using (tcpClient)
            using (NetworkStream networkStream = tcpClient.GetStream())
            {
                while (tcpClient.Connected)
                {
                    this.HandleClient(tcpClient, networkStream, floppyResolver, loader, saver);
                }
            }
        }

        private IPAddress GetLocalIPv4Address()
        {
            // Prefer a non-loopback, non-linklocal IPv4 address from active network interfaces
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus == OperationalStatus.Up))
            {
                var props = ni.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(addr.Address) &&
                        !addr.Address.IsIPv6LinkLocal)
                    {
                        return addr.Address;
                    }
                }
            }

            // Fallback to DNS resolution of host name
            var hostAddrs = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
            var ipv4 = hostAddrs.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
            return ipv4;
        }       
    }
}
