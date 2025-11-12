using System.Collections.Concurrent;
using System.Net;
using VDRIVE.Drive;
using VDRIVE.Floppy;
using VDRIVE.Protocol;
using VDRIVE.Util;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE
{
    public class HttpVDriveServer : IVDriveServer
    {
        public HttpVDriveServer(IConfiguration configuration, ILogger logger)
        {
            this.Configuration = configuration;
            this.Logger = logger;
        }
        protected IConfiguration Configuration;
        protected ILogger Logger;

        // session / client info for stateless HTTP
        private static ConcurrentDictionary<string, Session> VDriveClients = new ConcurrentDictionary<string, Session>();

        public void Start()
        {
            HttpListener httpListener = new HttpListener();
            httpListener.Prefixes.Add($"{this.Configuration.ServerListenAddress}{this.Configuration.ServerPort}/"); // WiC64 requires port 80 or 443
            httpListener.Start();

            this.Logger.LogMessage($"Listening on {this.Configuration.ServerListenAddress}:{this.Configuration.ServerPort}");

            while (true)
            {
                HttpListenerContext httpListenerContext = httpListener.GetContext();
                HttpListenerRequest httpListenerRequest = httpListenerContext.Request;
                HttpListenerResponse httpListenerResponse = httpListenerContext.Response;

                httpListenerResponse.SendChunked = false;
                httpListenerResponse.KeepAlive = true;

                Console.WriteLine($"{httpListenerRequest.HttpMethod} {httpListenerRequest.Url}");

                // HACK for now mapping the local IP as the Session ID until 
                // the full session management is in place so it will work
                // over internet.
                Session session = null;

                IPAddress clientIp = httpListenerContext.Request.RemoteEndPoint.Address;
                if (VDriveClients.ContainsKey(clientIp.ToString()))
                {
                    session = VDriveClients[clientIp.ToString()];
                }
                else
                {
                    // instance dependencies per client for concurrency
                    // and store in local session
                    session = new Session();
                    session.ProcessRunner = new LockingProcessRunner(this.Configuration, this.Logger);
                    session.FloppyResolver = FloppyResolverFactory.CreateFloppyResolver(this.Configuration.FloppyResolver, this.Configuration, this.Logger, session.ProcessRunner);
                    session.StorageAdapter = StorageAdapterFactory.CreateStorageAdapter(this.Configuration.StorageAdapter, session.ProcessRunner, this.Configuration, this.Logger);
                    session.ClientInfo = new ClientInfo();
                    session.ClientInfo.SessionId = clientIp.ToString();
                    session.ClientInfo.IPAddress = clientIp.ToString();

                    VDriveClients.GetOrAdd(clientIp.ToString(), session);
                }

                IProtocolHandler httpProtocolHandler = new HttpClientProtocolHandler(this.Configuration, this.Logger, httpListenerContext);
                httpProtocolHandler.HandleClient(session.FloppyResolver, session.StorageAdapter);
            }
        }
    }
}
