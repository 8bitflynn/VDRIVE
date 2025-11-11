using System.Net;
using System.Text;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Protocol
{
    public class HttpClientProtocolHandler : IProtocolHandler
    {
        public HttpClientProtocolHandler(IConfiguration configuration, ILogger logger, HttpListenerContext httpListenerContext)
        {
            this.Configuration = configuration;
            this.Logger = logger;
            this.HttpListenerContext = httpListenerContext;
        }
        private IConfiguration Configuration;
        private ILogger Logger;
        private HttpListenerContext HttpListenerContext;

        public void HandleClient(IFloppyResolver floppyResolver, IStorageAdapter storageAdapter)
        {
            try
            {
                var request = this.HttpListenerContext.Request;
                var response = this.HttpListenerContext.Response;

                response.SendChunked = false;
                response.KeepAlive = true;

                Console.WriteLine($"{request} {request.Url}");

                // --- MOUNT ---
                if (request.HttpMethod == "GET" &&
                    request.Url.AbsolutePath.Equals("/mount", StringComparison.OrdinalIgnoreCase))
                {
                    string sessionId = (request.QueryString["session"] ?? "default").ToUpper();
                    string imageId = request.QueryString["id"] ?? "?";

                    // Mimic: IVDriveCommandServer.Mount(sessionId, imageId)
                    this.Logger.LogMessage($"[Mount] session={sessionId}, image={imageId}");

                    FloppyIdentifier floppyIdentifier = new FloppyIdentifier
                    {
                        IdLo = byte.Parse(imageId),
                        IdHi = 0
                    };

                    FloppyInfo floppyInfo = floppyResolver.InsertFloppy(floppyIdentifier);
                    string fileName = new string(floppyInfo.ImageName).TrimEnd('\0');

                    string message = $"MOUNT OK (ID={imageId} {fileName})\r\n";

                    if (fileName.ToLower().EndsWith(".prg"))
                    {
                        message += $"\r\nLOAD \"*\",8,1";
                    }

                    string payload = "\r\n" + string.Concat(message) + "\r\n" + "\0";

                    WriteResponse(response, payload);
                    return;;
                }

                // --- SEARCH ---
                if (request.HttpMethod == "GET" &&
                    request.Url.AbsolutePath.Equals("/search", StringComparison.OrdinalIgnoreCase))
                {
                    string sessionId = request.QueryString["session"] ?? "default";
                    string term = request.QueryString["q"] ?? "";

                    SearchFloppiesRequest searchFloppiesRequest = new SearchFloppiesRequest();
                    searchFloppiesRequest.Operation = 5;
                    searchFloppiesRequest.SearchTerm = term.ToArray();
                    searchFloppiesRequest.SearchTermLength = (byte)term.Length;

                    SearchFloppyResponse searchFloppyResponse = floppyResolver.SearchFloppys(searchFloppiesRequest, out FloppyInfo[] foundFloppys);
                    if (searchFloppyResponse.ResultCount == 0)
                    {
                        string payload = "\r\n" + string.Concat("NO RESULTS FOUND\r\n") + "\0";
                        WriteResponse(response, payload);
                    }
                    else
                    {

                        var results = foundFloppys.Select(ff => $"{ff.IdLo} {new string(ff.ImageName).TrimEnd('\0')}\r\n");

                        string payload = "\r\n" + string.Concat(results) + "\0";
                        WriteResponse(response, payload);
                    }

                    // await WriteResponse(response, "HELLO FROM SERVER\0");


                    this.Logger.LogMessage($"[SEARCH] SESSION={sessionId}, TERM={term}");
                    return;
                }


                if (request.Url.AbsolutePath.Equals("/load", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string fileName = request.QueryString["file"] ?? "";
                        byte[] fullFile = null;

                        LoadRequest loadRequest = new LoadRequest();
                        loadRequest.Operation = 3;
                        loadRequest.FileName = fileName.TrimEnd().ToArray();
                        loadRequest.FileNameLength = (byte)fileName.Length;
                        storageAdapter.Load(loadRequest, floppyResolver, out fullFile);                       

                        ushort dest_ptr_start = 0x00;
                        if (fullFile != null)
                        {
                            byte[] dest_ptr_bytes = fullFile.Take(2).ToArray();
                            dest_ptr_start = (ushort)(fullFile[0] | (fullFile[1] << 8));
                            this.Logger.LogMessage($"Start Address: 0x{dest_ptr_start:X4}");

                            // check for known instant C64 crash addreses
                            // to help stabalize during testing
                            int end_dest_ptr = dest_ptr_start + fullFile.Length;

                            if (IsValidLoadAddress(this.Logger, dest_ptr_start, end_dest_ptr))
                            {
                                // fullFile = fullFile.Skip(2).ToArray(); // skip destination pointer    
                                int endAddress = dest_ptr_start + fullFile.Length - 1;
                                this.Logger.LogMessage($"End Address: 0x{endAddress:X4}");
                            }
                            else
                            {
                                fullFile = null;
                                //loadResponse.ResponseCode = 0x04; // file not found
                            }
                        }


                        HandleRequestAsync(this.HttpListenerContext, fullFile);

                        fullFile = null;
                    }
                    catch (Exception ex)
                    {
                        this.Logger.LogMessage(ex.Message);
                    }

                    return; // prevent falling through to 404

                }

                // --- SAVE ---
                if (request.HttpMethod == "POST" &&
                    request.Url.AbsolutePath.Equals("/save", StringComparison.OrdinalIgnoreCase))
                {
                    string sessionId = request.QueryString["session"] ?? "default";
                    string fileName = request.QueryString["file"] ?? "unknown";

                    using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                    var body = reader.ReadToEnd();
                    byte[] data = Encoding.ASCII.GetBytes(body);

                    // Mimic: Save(sessionId, fileName, data)
                    this.Logger.LogMessage($"[Save] session={sessionId}, file={fileName}, length={data.Length}");

                    WriteResponse(response, "SAVE OK");
                    return;
                }

                // --- Default 404 ---
                response.StatusCode = 404;
                 WriteResponse(response, "Not Found");
            }
            catch (Exception ex)
            {
                this.Logger.LogMessage($"[Error] {ex.Message}");
            }
        }

        private static bool IsValidLoadAddress(ILogger logger, ushort dest_ptr_start, int end_dest_ptr)
        {
            // TODO: for now returning a file not found
            // but need to investigate better handling later
            List<ushort> rejectedLoadAddresses = new List<ushort>()
                                {
                                    0x0314, // BASIC IRQ
                                    0x0316, // BASIC NMI
                                    0xFFFE, // KERNAL IRQ
                                    0xFFFA, // KERNAL NMI
                                    0xEA38, // LOAD address IRQ
                                    0xC000  // VDRIVE location
                                };

            if (rejectedLoadAddresses.Any(r => r == dest_ptr_start)
                || end_dest_ptr >= 0xc000)
            {
                logger.LogMessage($"Warning: invalid load address or end address 0x{dest_ptr_start:X4}-0x{end_dest_ptr:X4}, rejecting load to prevent C64 lockup");
                return false;
            }

            return true;

        }

        async Task HandleRequestAsync(HttpListenerContext ctx, byte[] payload)
        {
            var resp = ctx.Response;
            resp.SendChunked = false;
            resp.ContentLength64 = payload == null ? 0 : payload.Length;
            await resp.OutputStream.WriteAsync(payload, 0, payload.Length);
            await resp.OutputStream.FlushAsync();
            resp.Close();

            // If you must wait before accepting the next chunk for some reason,
            // do it outside of the request thread (e.g. scheduler, not Thread.Sleep).
            // await Task.Delay(35);   // non-blocking
        }

        private async Task WriteResponse(HttpListenerResponse response, string text)
        {
            byte[] msg = Encoding.ASCII.GetBytes(text + "\0"); // mimic null-terminated strings
            response.StatusCode = 200;
            response.ContentType = "text/plain";
            response.ContentLength64 = msg.Length;
            await response.OutputStream.WriteAsync(msg, 0, msg.Length);
            response.OutputStream.Flush();
            response.Close();
        }
    }
}
