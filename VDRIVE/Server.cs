using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE
{
    public class Server : VDriveBase, IServer
    {
        public Server(IConfiguration configuation, IFloppyResolver floppyResolver, ILoad loader, ISave saver, 
            ILog logger, string listenIp = null, int port = 6510)
        {
            this.Configuration = configuation;
            this.FloppyResolver = floppyResolver;
            this.Loader = loader;
            this.Saver = saver;
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

        public void Start()
        {
            TcpListener listener = new TcpListener(this.ListenAddress, Port);
            listener.Start();

            this.Logger.LogMessage($"Listening on {this.ListenAddress}:{Port}");

            while (true)
            {
                TcpClient client = listener.AcceptTcpClient();
                client.NoDelay = true;
                Task.Run(() => this.HandleClient(client));
            }
        }

        private void HandleClient(TcpClient tcpClient)
        {
            string ip = tcpClient.Client.RemoteEndPoint.ToString();

            this.Logger.LogMessage($"Client connected: {ip}");
            
            using (tcpClient)
            using (NetworkStream networkStream = tcpClient.GetStream())
            {
                while (tcpClient.Connected)
                {
                    this.HandleClient(tcpClient, networkStream);
                }              
            }
        }
    }
}
