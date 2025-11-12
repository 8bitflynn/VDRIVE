using System.Net;
using System.Net.Sockets;
using System.Text;
using VDRIVE;
using VDRIVE.Configuration;
using VDRIVE.Drive;
using VDRIVE.Floppy;
using VDRIVE.Util;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE_Host
{
    public class Program
    {
        /// <summary>
        /// 
        /// CURRENT TESTING on C64
        /// SYS 49152 - enable
        /// SYS 49158 - search floppies (will prompt for search term in C64, enter the number to mount)
        /// SYS 49161 - insert floppy (will prompt for ID in C64) - already done i n previous step but user can change floppy here
        /// </summary>
        /// <param name="args"></param>
        //static void Main(string[] args)
        //{
        //    ILogger logger = new VDRIVE.Util.ConsoleLogger();

        //    VDRIVE_Contracts.Interfaces.IConfigurationBuilder configBuilder = new ConfigurationBuilder(logger);
        //    VDRIVE_Contracts.Interfaces.IConfiguration configuration = configBuilder.BuildConfiguration();

        //    if (!configBuilder.IsValidConfiguration(configuration))
        //    {
        //        logger.LogMessage("Invalid configuration, exiting");
        //        return;
        //    }

        //    configBuilder.DumpConfiguration(configuration);

        //    switch (configuration.ServerOrClientMode)
        //    {
        //        case "Server":
        //            // firmware is setup as client mode by default so run this in server mode
        //            // should allow multiple C64 connections to same disk image but
        //            // might need to put some locks in place for anything shared access         
        //            IVDriveServer server = new VDriveServer(configuration, logger);
        //            server.Start();
        //            break;

        //        case "Client":
        //            // client mode is nice if you cannot change firewall settings as ESP8266 does not have a firewall!                 
        //            IVDriveClient client = new VDriveClient(configuration, logger);
        //            client.Start();
        //            break;
        //    }
        //}




        static async Task Main()
        {
            byte[] fullFile = null;//; File.ReadAllBytes(@"c:\\temp\\ghostsgoblins.prg");
            string loadedFileName = ""; // hack to test dynamic loading

            DateTime startLoad = DateTime.MinValue;

            ILogger logger = new VDRIVE.Util.ConsoleLogger();

            VDRIVE_Contracts.Interfaces.IConfigurationBuilder configBuilder = new ConfigurationBuilder(logger);
            VDRIVE_Contracts.Interfaces.IConfiguration configuration = configBuilder.BuildConfiguration();

            if (!configBuilder.IsValidConfiguration(configuration))
            {
                logger.LogMessage("Invalid configuration, exiting");
                return;
            }

            configBuilder.DumpConfiguration(configuration);

            Task.Run(() =>
            {
                switch (configuration.ServerOrClientMode)
                {
                    case "Server":
                        // firmware is setup as client mode by default so run this in server mode
                        // should allow multiple C64 connections to same disk image but
                        // might need to put some locks in place for anything shared access         
                        IVDriveServer server = new VDriveServer(configuration, logger);
                        server.Start();
                        break;

                    case "Client":
                        // client mode is nice if you cannot change firewall settings as ESP8266 does not have a firewall!                 
                        IVDriveClient client = new VDriveClient(configuration, logger);
                        client.Start();
                        break;
                }
            });


            IProcessRunner processRunner = new LockingProcessRunner(configuration, logger);
            IFloppyResolver floppyResolver = FloppyResolverFactory.CreateFloppyResolver(configuration.FloppyResolver, configuration, logger, processRunner);
            IStorageAdapter storageAdapter = StorageAdapterFactory.CreateStorageAdapter(configuration.StorageAdapter, processRunner, configuration, logger);

            // define and start the listener
            var listener = new HttpListener();
            listener.Prefixes.Add("http://*:80/"); // use 8080 unless you want to run as admin
            listener.Start();
            logger.LogMessage("VDRIVE listening on port 80...");


            while (true)
            {
                try
                {
                    var context = await listener.GetContextAsync();
                    var request = context.Request;
                    var response = context.Response;

                    response.SendChunked = false;
                    response.KeepAlive = true;

                    Console.WriteLine($"{request.HttpMethod} {request.Url}");

                    // --- MOUNT ---
                    if (request.HttpMethod == "GET" &&
                        request.Url.AbsolutePath.Equals("/mount", StringComparison.OrdinalIgnoreCase))
                    {
                        string sessionId = (request.QueryString["session"] ?? "default").ToUpper();
                        string imageId = request.QueryString["id"] ?? "?";

                        // Mimic: IVDriveCommandServer.Mount(sessionId, imageId)
                        logger.LogMessage($"[Mount] session={sessionId}, image={imageId}");

                        FloppyIdentifier floppyIdentifier = new FloppyIdentifier
                        {
                            IdLo = byte.Parse(imageId),
                            IdHi = 0
                        };

                        floppyResolver.InsertFloppy(floppyIdentifier);

                        await WriteResponse(response, $"MOUNT OK (SESSION={sessionId}, IMAGE={imageId})");
                        continue;
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

                        var results = foundFloppys.Select(ff => $"{ff.IdLo} {new string(ff.ImageName).TrimEnd('\0')}\r\n");

                        string payload = "\r\n" + string.Concat(results) + "\0";
                        await WriteResponse(response, payload);

                        // await WriteResponse(response, "HELLO FROM SERVER\0");


                        logger.LogMessage($"[SEARCH] SESSION={sessionId}, TERM={term}");
                        continue;
                    }


                    if (request.Url.AbsolutePath.Equals("/load", StringComparison.OrdinalIgnoreCase))
                    {
                        //FloppyInfo floppyInfo = floppyResolver.GetInsertedFloppyInfo();
                        string fileName = request.QueryString["file"] ?? "";

                        //string offHex = request.QueryString["off"] ?? "$0000";

                        if (fullFile == null || (fileName != loadedFileName))
                        {
                            startLoad = DateTime.Now;

                            LoadRequest loadRequest = new LoadRequest();
                            loadRequest.Operation = 3;
                            loadRequest.FileName = fileName.TrimEnd().ToArray();
                            loadRequest.FileNameLength = (byte)fileName.Length;
                            storageAdapter.Load(loadRequest, floppyResolver, out fullFile);
                            loadedFileName = fileName;
                        }


                        //int offset = 0;
                        //if (offHex.StartsWith("$"))
                        //    int.TryParse(offHex.Substring(1),
                        //                 System.Globalization.NumberStyles.HexNumber,
                        //                 null, out offset);

                        //const int chunkSize = 192;
                        //int thisChunk;
                        //byte[] payload;

                        //// Map client's offset (bytes written so far) to file index in fullFile.
                        //// Client's offset==0 should map to file index 2 (skip PRG header).
                        //int fileIndex = 2 + offset;                      // ALWAYS skip the 2-byte PRG header

                        //int remaining = Math.Max(0, fullFile.Length - fileIndex);
                        //thisChunk = Math.Min(chunkSize, remaining);

                        //// Build payload: first byte = count, followed by chunk bytes
                        //payload = new byte[thisChunk + 1];
                        //payload[0] = (byte)(thisChunk & 0xff);
                        ////payload[1] = (byte)((thisChunk >> 8) & 0xff);
                        //if (thisChunk > 0)
                        //    Buffer.BlockCopy(fullFile, fileIndex, payload, 1, thisChunk);

                        //logger.LogMessage($"[Load] off={offHex}, fileIndex={fileIndex}, count={thisChunk}");


                        // byte[] payload = File.ReadAllBytes(Path.Combine(@"c:\temp\", fileName));



                        response.StatusCode = 200;
                        response.ContentType = "application/octet-stream";
                        response.ContentLength64 = fullFile.Length;


                        HandleRequestAsync(context, fullFile);

                        // response.OutputStream.Write(payload, 0, payload.Length);
                        //foreach (byte b in payload)
                        //{
                        //    response.OutputStream.WriteByte(b);
                        //}
                        // await response.OutputStream.WriteAsync(payload, 0, payload.Length);
                        //response.OutputStream.Flush();
                        //response.Close();

                        //  await Task.Delay(35); // tune 2..10 ms

                        //if (thisChunk == 0)
                        //{
                        //    var totalTime = DateTime.Now - startLoad;
                        //    int bytesPerSecond = (int)(fullFile.Length / totalTime.TotalSeconds);
                        //    logger.LogMessage($"[Load] Completed loading '{fileName}' ({fullFile.Length} bytes) {totalTime.TotalSeconds} BytesPerSecond: {bytesPerSecond} ");
                        //    payload = null;
                        //    loadedFileName = "";

                        //}

                        continue; // prevent falling through to 404
                    }

                    // --- SAVE ---
                    if (request.HttpMethod == "POST" &&
                        request.Url.AbsolutePath.Equals("/save", StringComparison.OrdinalIgnoreCase))
                    {
                        string sessionId = request.QueryString["session"] ?? "default";
                        string fileName = request.QueryString["file"] ?? "unknown";

                        using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
                        var body = await reader.ReadToEndAsync();
                        byte[] data = Encoding.ASCII.GetBytes(body);

                        // Mimic: Save(sessionId, fileName, data)
                        logger.LogMessage($"[Save] session={sessionId}, file={fileName}, length={data.Length}");

                        await WriteResponse(response, "SAVE OK");
                        continue;
                    }

                    // --- Default 404 ---
                    response.StatusCode = 404;
                    await WriteResponse(response, "Not Found");
                }
                catch (Exception ex)
                {
                    logger.LogMessage($"[Error] {ex.Message}");
                }
            }       

        }

        async static Task HandleRequestAsync(HttpListenerContext ctx, byte[] payload)
        {
            var resp = ctx.Response;
            resp.SendChunked = false;
            resp.ContentLength64 = payload.Length;
            await resp.OutputStream.WriteAsync(payload, 0, payload.Length);
            await resp.OutputStream.FlushAsync();
            resp.Close();

            // If you must wait before accepting the next chunk for some reason,
            // do it outside of the request thread (e.g. scheduler, not Thread.Sleep).
           // await Task.Delay(35);   // non-blocking
        }

        static async Task WriteResponse(HttpListenerResponse response, string text)
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
