using System.Net;
using System.Text;
using VDRIVE.Floppy.Impl;
using VDRIVE.Util;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;
using VDRIVE_Contracts.Structures.Http;

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

        public void HandleClient(ISessionProvider sessionManager)
        {
            try
            {
                HttpListenerRequest httpListenerRequest = this.HttpListenerContext.Request;
                HttpListenerResponse httpListenerResponse = this.HttpListenerContext.Response;

                httpListenerResponse.SendChunked = false;
                httpListenerResponse.KeepAlive = true;

                this.Logger.LogMessage($"{httpListenerRequest.HttpMethod} {httpListenerRequest.Url}");

                // LOAD
                if (httpListenerRequest.HttpMethod == "POST" && httpListenerRequest.Url.AbsolutePath.Equals("/load", StringComparison.OrdinalIgnoreCase))
                {
                    // TODO: if session ID is 0 return an error
                    // for now, only search can create a new session

                    byte[] loadBytes = ParseMultipartDataBytes(httpListenerRequest);

                    HttpLoadRequest loadRequest;
                    try
                    {
                        loadRequest = HttpLoadRequest.ParseFromBytes(loadBytes);
                    }
                    catch (ArgumentException ex)
                    {
                        this.Logger.LogMessage($"[LOAD] Invalid request: {ex.Message}");
                        
                        LoadResponse errorResponse = new LoadResponse { ResponseCode = 0x04 };
                        WriteLoadResponse(this.HttpListenerContext, new byte[0], errorResponse, 
                            sessionManager.GetOrCreateSession(0)); // Use default session for errors
                        return;
                    }

                    string fileName = loadRequest.GetFilenameString().TrimEnd();
                    Session session = sessionManager.GetOrCreateSession(loadRequest.SessionId);

                    LoadResponse loadResponse = new LoadResponse { ResponseCode = 0x04 }; // File not found
                    byte[] responsePayload = new byte[0]; // Empty payload for errors

                    if (!string.IsNullOrEmpty(fileName))
                    {
                        DateTime start = DateTime.Now;
                        this.Logger.LogMessage($"Loading '{fileName}' (length={fileName.Length}) starting");

                        LoadRequest loadRequestInternal = new LoadRequest();
                        loadRequestInternal.Operation = 3;
                        loadRequestInternal.FileName = fileName.ToArray();
                        loadRequestInternal.FileNameLength = (byte)fileName.Length;
                        
                        loadResponse = session.StorageAdapter.Load(loadRequestInternal, session.FloppyResolver, out byte[] fullFile);

                        if (fullFile != null && loadResponse.ResponseCode == 0xff)
                        {
                            ushort dest_ptr_start = (ushort)(fullFile[0] | (fullFile[1] << 8));
                            this.Logger.LogMessage($"Start Address: 0x{dest_ptr_start:X4}");

                            int endAddress = dest_ptr_start + fullFile.Length - 1;
                            this.Logger.LogMessage($"End Address: 0x{endAddress:X4}");
                            
                            responsePayload = fullFile;
                        }
                        else
                        {
                            this.Logger.LogMessage($"Load failed with response code: 0x{loadResponse.ResponseCode:X2}");
                        }
                    }
                    else
                    {
                        this.Logger.LogMessage("[Load] Invalid request - empty filename");
                    }

                    // Always send response - never return without responding
                    WriteLoadResponse(this.HttpListenerContext, responsePayload, loadResponse, session);
                    return;
                }

                // SAVE (receives single POST with combined payload: [sessionId][filename_length][filename][prg header][data])
                if (httpListenerRequest.HttpMethod == "POST" && httpListenerRequest.Url.AbsolutePath.Equals("/save", StringComparison.OrdinalIgnoreCase))
                {
                    // TODO: if session ID is 0 return an error
                    // search creates the session, load/save require valid session

                    byte[] saveBytes = ParseMultipartDataBytes(httpListenerRequest);

                    // Use HttpSaveRequest for clean parsing
                    HttpSaveRequest saveRequest;
                    try
                    {
                        saveRequest = HttpSaveRequest.ParseFromBytes(saveBytes);
                    }
                    catch (ArgumentException ex)
                    {
                        this.Logger.LogMessage($"[SAVE] Invalid request: {ex.Message}");
                        // Send error response but don't hang the client
                        WriteSaveResponse(httpListenerResponse, "ERROR: Invalid save request", new SaveResponse { ResponseCode = 0x04 }, null);
                        return;
                    }

                    // Extract clean data from parsed request
                    string fileName = saveRequest.GetFilenameString().TrimEnd();
                    byte[] fileData = saveRequest.FileData;

                    Session session = sessionManager.GetOrCreateSession(saveRequest.SessionId);

                    this.Logger.LogMessage($"[Save] Filename: {fileName}, File data: {fileData?.Length ?? 0} bytes");

                    if (fileData == null || fileData.Length < 2)
                    {
                        WriteSaveResponse(httpListenerResponse, "ERROR: Invalid file data", new SaveResponse { ResponseCode = 0x04 }, session);
                        return;
                    }

                    // Create save request
                    SaveRequest saveRequestInternal = new SaveRequest();
                    saveRequestInternal.Operation = 4;
                    saveRequestInternal.FileName = fileName.ToArray();
                    saveRequestInternal.FileNameLength = (byte)fileName.Length;
                    saveRequestInternal.DeviceNum = 8; // defaulting for now
                    saveRequestInternal.SecondaryAddr = 1; // defaulting for now
                    saveRequestInternal.TargetAddressLo = fileData[0];
                    saveRequestInternal.TargetAddressHi = fileData[1];
                    byte[] payload = fileData.Skip(2).ToArray();

                    Logger.LogMessage($"Save Request: SAVE\"{fileName}\",{saveRequestInternal.DeviceNum}{(saveRequestInternal.SecondaryAddr != 0 ? "," + saveRequestInternal.SecondaryAddr : "")}");

                    SaveResponse saveResponse = session.StorageAdapter.Save(saveRequestInternal, session.FloppyResolver, payload);

                    // TODO: return proper error codes
                    string payloadResponse = "\r\n" + string.Concat("SAVE OK") + "\r\n" + "\0";

                    WriteSaveResponse(httpListenerResponse, payloadResponse, saveResponse, session);
                    return;
                }

                // SEARCH
                if (httpListenerRequest.HttpMethod == "POST" && httpListenerRequest.Url.AbsolutePath.Equals("/search", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] searchFloppyBytes = ParseMultipartDataBytes(httpListenerRequest);

                    // Use HttpSearchFloppyRequest for clean parsing
                    HttpSearchFloppyRequest searchRequest;
                    try
                    {
                        searchRequest = HttpSearchFloppyRequest.ParseFromBytes(searchFloppyBytes);
                    }
                    catch (ArgumentException ex)
                    {
                        this.Logger.LogMessage($"[SEARCH] Invalid request: {ex.Message}");
                        // Send error response but don't hang the client
                        WriteResponse(httpListenerResponse, "ERROR: Invalid search request", null);
                        return;
                    }

                    // Extract clean data from parsed request
                    string searchTerm = Encoding.ASCII.GetString(searchRequest.SearchTerm, 0, searchRequest.SearchTermLength).TrimEnd();
                    
                    this.Logger.LogMessage($"[SEARCH] TERM={searchTerm} *** [SESSIONID] = {searchRequest.SessionId}");

                    // search is entry to create new session as all other operations
                    // need a set of "found" floppies so that mount can refer to them
                    // even if session ID is 0, we create a new session
                    // load/save will return an error if session ID is 0
                    Session session = sessionManager.GetOrCreateSession(searchRequest.SessionId);

                    SearchFloppiesRequest searchFloppiesRequest = new SearchFloppiesRequest();
                    searchFloppiesRequest.Operation = 5;
                    searchFloppiesRequest.SearchTerm = searchTerm.ToArray();
                    searchFloppiesRequest.SearchTermLength = (byte)searchTerm.Length;

                    SearchFloppyResponse searchFloppyResponse = session.FloppyResolver.SearchFloppys(searchFloppiesRequest, out FloppyInfo[] foundFloppys);
                    if (searchFloppyResponse.ResultCount == 0)
                    {
                        string payload = "\r\n" + string.Concat("NO RESULTS FOUND\r\n") + "\0";
                        WriteResponse(httpListenerResponse, payload, session);
                    }
                    else
                    {
                        var results = foundFloppys.Select(ff => $"{ff.IdLo} {new string(ff.ImageName).TrimEnd('\0')}\r\n");
                        string payload = $"\r\n\r\nSESSION-ID:{session.SessionId}\r\n\r\n" + string.Concat(results) + "\0";

                        // TODO: make sure payload is less than max size for a single page
                        WriteResponse(httpListenerResponse, payload, session);
                    }

                    return;
                }

                // MOUNT
                if (httpListenerRequest.HttpMethod == "POST" && httpListenerRequest.Url.AbsolutePath.Equals("/mount", StringComparison.OrdinalIgnoreCase))
                {
                    // TODO: if session ID is 0 return an error
                    // since mounting requires a valid session

                    byte[] mountBytes = ParseMultipartDataBytes(httpListenerRequest);

                    // Use HttpMountRequest for clean parsing
                    HttpMountRequest mountRequest;
                    try
                    {
                        mountRequest = HttpMountRequest.ParseFromBytes(mountBytes);
                    }
                    catch (ArgumentException ex)
                    {
                        this.Logger.LogMessage($"[MOUNT] Invalid request: {ex.Message}");
                        // Send error response but don't hang the client
                        WriteResponse(httpListenerResponse, "ERROR: Invalid mount request", null);
                        return;
                    }

                    // Extract clean data from parsed request
                    string imageId = mountRequest.GetImageIdString().TrimEnd();
                    
                    this.Logger.LogMessage($"[Mount] image={imageId}");

                    Session session = sessionManager.GetOrCreateSession(mountRequest.SessionId);

                    // FIXME: only showing single page of results
                    // need to implement paging
                    FloppyIdentifier floppyIdentifier = new FloppyIdentifier
                    {
                        IdLo = byte.Parse(imageId),
                        IdHi = 0
                    };

                    FloppyInfo floppyInfo = session.FloppyResolver.InsertFloppy(floppyIdentifier);
                    string fileName = new string(floppyInfo.ImageName).TrimEnd('\0');

                    string message = $"MOUNT OK (ID={imageId} {fileName})\r\n";

                    // HACK: returning typical load commands based on type
                    // to save some typing on C64
                    if (fileName.ToLower().EndsWith("prg") ||
                        session.FloppyResolver.GetType() == typeof(HvscPsidFloppyResolver))
                    {
                        message += $"\r\nLOAD \"*\",8,1";
                    }
                    else
                    {
                        message += $"\r\nLOAD \"$\",8";
                    }

                    string payload = "\r\n" + string.Concat(message) + "\r\n" + "\0";

                    WriteResponse(httpListenerResponse, payload, session);
                    return;
                }

                // --- Default 404 ---
                this.Logger.LogMessage($"[404] {httpListenerRequest.HttpMethod} {httpListenerRequest.Url}");
                httpListenerResponse.StatusCode = 404;
                WriteResponse(httpListenerResponse, "Not Found");
            }
            catch (Exception ex)
            {
                this.Logger.LogMessage($"[Error] {ex.Message}");
            }
        }

        private byte[] ParseMultipartDataBytes(HttpListenerRequest request)
        {
            if (request.ContentType?.Contains("multipart/form-data") != true)
            {
                using (var memoryStream = new MemoryStream())
                {
                    request.InputStream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                }
            }

            // Read entire body as bytes
            byte[] fullBody;
            using (var memoryStream = new MemoryStream())
            {
                request.InputStream.CopyTo(memoryStream);
                fullBody = memoryStream.ToArray();
            }

            // Find the "data" field by searching for the header pattern in bytes (NOT string)
            byte[] headerPattern = Encoding.ASCII.GetBytes("Content-Disposition: form-data; name=\"data\"");
            int headerStart = FindBytes(fullBody, headerPattern);

            if (headerStart < 0)
                return new byte[0];

            // Find the end of headers (CRLF CRLF = \r\n\r\n)
            byte[] crlfcrlf = new byte[] { 13, 10, 13, 10 };
            int dataStart = FindBytes(fullBody, crlfcrlf, headerStart);
            if (dataStart < 0)
                return new byte[0];

            dataStart += 4; // Skip past the \r\n\r\n

            // Find the boundary to extract just the payload
            string boundary = "--" + request.ContentType.Split(new[] { "boundary=" }, StringSplitOptions.None)[1];
            byte[] boundaryBytes = Encoding.ASCII.GetBytes(boundary);
            int dataEnd = FindBytes(fullBody, boundaryBytes, dataStart);
            if (dataEnd < 0)
                dataEnd = fullBody.Length;

            // Back up over any trailing CRLF before the boundary
            while (dataEnd > dataStart && (fullBody[dataEnd - 1] == 10 || fullBody[dataEnd - 1] == 13))
                dataEnd--;

            // Extract the data as pure bytes (NO string conversion)
            int length = dataEnd - dataStart;
            if (length <= 0)
                return new byte[0];

            byte[] result = new byte[length];
            Array.Copy(fullBody, dataStart, result, 0, length);
            return result;
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

        private void WriteLoadResponse(HttpListenerContext httpListenerContext, byte[] filePayload, LoadResponse loadResponse, Session session)
        {
            var resp = httpListenerContext.Response;
            resp.SendChunked = false;
            resp.StatusCode = 200;
            resp.ContentType = "application/octet-stream";

            // Create HTTP response header
            HttpLoadResponse httpResponse = HttpLoadResponse.Create(
                session?.SessionId ?? 0,
                loadResponse.ResponseCode
            );

            // Serialize header + payload
            List<byte> fullResponse = new List<byte>();
            fullResponse.AddRange(BinaryStructConverter.ToByteArray(httpResponse));
            
            if (filePayload != null)
            {
                fullResponse.AddRange(filePayload);
            }

            // Write response
            resp.ContentLength64 = fullResponse.Count;
            resp.OutputStream.Write(fullResponse.ToArray(), 0, fullResponse.Count);
            resp.OutputStream.Flush();
            resp.Close();
        }

        private void WriteSaveResponse(HttpListenerResponse response, string text, SaveResponse saveResponse, Session session = null)
        {
            // Create HTTP response header
            HttpSaveResponse httpResponse = HttpSaveResponse.Create(
                session?.SessionId ?? 0,
                saveResponse.ResponseCode
            );

            // Serialize header + text payload
            List<byte> fullResponse = new List<byte>();
            fullResponse.AddRange(BinaryStructConverter.ToByteArray(httpResponse));
            
            // Add text payload
            if (!string.IsNullOrEmpty(text))
            {
                fullResponse.AddRange(Encoding.ASCII.GetBytes(text));
            }

            response.StatusCode = 200;
            response.ContentType = "text/plain";
            response.ContentLength64 = fullResponse.Count;

            response.OutputStream.Write(fullResponse.ToArray(), 0, fullResponse.Count);
            response.OutputStream.Flush();
            response.Close();
        }

        private void WriteResponse(HttpListenerResponse response, string text, Session session = null)
        {
            byte[] fullResponse = Encoding.ASCII.GetBytes(text);

            // HACK: add session ID to start of payload        
            if (session != null)
            {
                List<byte> msgWithSession = new List<byte>();
                msgWithSession.Add((byte)(session.SessionId & 0xFF));
                msgWithSession.Add((byte)(session.SessionId >> 8));
                msgWithSession.AddRange(fullResponse);
                fullResponse = msgWithSession.ToArray();
            }

            response.StatusCode = 200;
            response.ContentType = "text/plain";
            response.ContentLength64 = fullResponse.Length;

            response.OutputStream.Write(fullResponse, 0, fullResponse.Length);
            response.OutputStream.Flush();
            response.Close();
        }

        private int FindBytes(byte[] haystack, byte[] needle, int start = 0)
        {
            for (int i = start; i < haystack.Length - needle.Length + 1; i++)
            {
                bool found = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                    return i;
            }
            return -1;
        }
    }
}
