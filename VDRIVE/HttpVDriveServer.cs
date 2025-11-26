using System.Collections.Concurrent;
using System.Net;
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
        private static readonly ConcurrentDictionary<string, Session> VDriveClients = new ConcurrentDictionary<string, Session>();

        public void Start()
        {
            HttpListener httpListener = new HttpListener();
            httpListener.Prefixes.Add($"{this.Configuration.ServerListenAddress}{this.Configuration.ServerPort}/"); // WiC64 requires port 80 or 443
            httpListener.Start();

            this.Logger.LogMessage($"Listening on {this.Configuration.ServerListenAddress}:{this.Configuration.ServerPort}");
              
            while (true)
            {
                try
                {
                    HttpListenerContext httpListenerContext = httpListener.GetContext(); // blocking
                    
                    // Handle each request on a separate thread to avoid blocking
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            HttpListenerRequest httpListenerRequest = httpListenerContext.Request;
                            HttpListenerResponse httpListenerResponse = httpListenerContext.Response;

                            httpListenerResponse.SendChunked = false;
                            httpListenerResponse.KeepAlive = true;                           
                            
                            ISessionProvider sessionProvider = new SessionProvider(this.Configuration, this.Logger);
                            IProtocolHandler httpProtocolHandler = new HttpClientProtocolHandler(this.Configuration, this.Logger, httpListenerContext);
                            httpProtocolHandler.HandleClient(sessionProvider);
                        }
                        catch (Exception ex)
                        {
                            this.Logger.LogMessage($"Error handling request: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Error);
                        }
                    });
                }
                catch (Exception ex)
                {
                    this.Logger.LogMessage($"Error accepting connection: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Error);
                }
            }
        }      
    }
}
