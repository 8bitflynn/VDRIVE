using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using VDRIVE.Drive;
using VDRIVE.Floppy;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE
{
    public class VDriveServer : IVDriveServer
    {
        public VDriveServer(IConfiguration configuation, ILogger logger)
        {
            this.Configuration = configuation;
            this.Logger = logger;

            if (!string.IsNullOrWhiteSpace(this.Configuration.ServerListenAddress))
            {
                this.ListenAddress = IPAddress.Parse(this.Configuration.ServerListenAddress);
            } 
            else
            {
                this.ListenAddress = this.GetLocalIPv4Address();
            }

            this.Port = this.Configuration.ServerPort.Value;
            if (this.Port == -1)
            {
                this.Port = this.Configuration.ServerPort.Value;
            }
        }
        private readonly IPAddress ListenAddress;
        private readonly int Port;
        protected IConfiguration Configuration;
        protected ILogger Logger;


        public void Start()
        {
            TcpListener listener = new TcpListener(this.ListenAddress, Port);
            listener.Start();

            this.Logger.LogMessage($"Listening on {this.ListenAddress}:{Port}");

            while (true)
            {
                TcpClient tcpClient = listener.AcceptTcpClient(); // blocking
                tcpClient.NoDelay = true;              

                Task.Run(() => 
                {
                    // instance dependencies per client for concurrency
                    IProtocolHandler potocolHandler = new ProtocolHandler(this.Configuration, this.Logger);
                    IFloppyResolver floppyResolver = FloppyResolverFactory.CreateFloppyResolver(this.Configuration.FloppyResolver, this.Configuration, this.Logger);
                    IStorageAdapter storageAdapter = StorageAdapterFactory.CreateStorageAdapter(this.Configuration.StorageAdapter, this.Configuration, this.Logger);                  

                    string ip = tcpClient.Client.RemoteEndPoint.ToString();
                    this.Logger.LogMessage($"Client connected: {ip}");

                    using (tcpClient)
                    using (NetworkStream networkStream = tcpClient.GetStream())
                    {
                        while (tcpClient.Connected)
                        {
                            potocolHandler.HandleClient(tcpClient, networkStream, floppyResolver, storageAdapter);
                        }
                    }
                });
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
