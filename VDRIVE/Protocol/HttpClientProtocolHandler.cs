using System.Net;
using System.Net.Sockets;
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
        private const int MAX_EXPECTED_PAYLOAD_SIZE = 64 * 1024; 
        private const int READ_TIMEOUT_MS = 3000; 
        private const int MAX_READ_ATTEMPTS = 5;
        private const int RESPONSE_WRITE_TIMEOUT_MS = 5000; // 5 seconds to write response
        
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
                        WriteLoadResponse(this.HttpListenerContext, new byte[0], errorResponse, sessionManager.GetOrCreateSession(0));
                        return;
                    }

                    string fileName = loadRequest.GetFilenameString().TrimEnd();
                    Session session = sessionManager.GetOrCreateSession(loadRequest.SessionId);

                    LoadResponse loadResponse = new LoadResponse { ResponseCode = 0x04 };
                    byte[] responsePayload = new byte[0];

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

                            if (!IsValidLoadAddress(this.Logger, dest_ptr_start, endAddress))
                            {                    
                                loadResponse.ResponseCode = 0x04;
                                responsePayload = new byte[0];
                            }
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
                   
                    WriteLoadResponse(this.HttpListenerContext, responsePayload, loadResponse, session);
                    return;
                }

                // SAVE
                if (httpListenerRequest.HttpMethod == "POST" && httpListenerRequest.Url.AbsolutePath.Equals("/save", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] saveBytes = ParseMultipartDataBytes(httpListenerRequest);

                    HttpSaveRequest saveRequest;
                    try
                    {
                        saveRequest = HttpSaveRequest.ParseFromBytes(saveBytes);
                    }
                    catch (ArgumentException ex)
                    {
                        this.Logger.LogMessage($"[SAVE] Invalid request: {ex.Message}");
                        WriteSaveResponse(httpListenerResponse, "ERROR: Invalid save request", new SaveResponse { ResponseCode = 0x04 }, null);
                        return;
                    }

                    string fileName = saveRequest.GetFilenameString().TrimEnd();
                    byte[] fileData = saveRequest.FileData;

                    Session session = sessionManager.GetOrCreateSession(saveRequest.SessionId);

                    this.Logger.LogMessage($"[Save] Filename: {fileName}, File data: {fileData?.Length ?? 0} bytes");

                    if (fileData == null || fileData.Length < 2)
                    {
                        WriteSaveResponse(httpListenerResponse, "ERROR: Invalid file data", new SaveResponse { ResponseCode = 0x04 }, session);
                        return;
                    }

                    SaveRequest saveRequestInternal = new SaveRequest();
                    saveRequestInternal.Operation = 4;
                    saveRequestInternal.FileName = fileName.ToArray();
                    saveRequestInternal.FileNameLength = (byte)fileName.Length;
                    saveRequestInternal.DeviceNum = 8;
                    saveRequestInternal.SecondaryAddr = 1;
                    saveRequestInternal.TargetAddressLo = fileData[0];
                    saveRequestInternal.TargetAddressHi = fileData[1];
                    byte[] payload = fileData.Skip(2).ToArray();

                    Logger.LogMessage($"Save Request: SAVE\"{fileName}\",{saveRequestInternal.DeviceNum}{(saveRequestInternal.SecondaryAddr != 0 ? "," + saveRequestInternal.SecondaryAddr : "")}");

                    SaveResponse saveResponse = session.StorageAdapter.Save(saveRequestInternal, session.FloppyResolver, payload);

                    string payloadResponse = "\r\n" + string.Concat("SAVE OK") + "\r\n" + "\0";

                    WriteSaveResponse(httpListenerResponse, payloadResponse, saveResponse, session);
                    return;
                }

                // SEARCH
                if (httpListenerRequest.HttpMethod == "POST" && httpListenerRequest.Url.AbsolutePath.Equals("/search", StringComparison.OrdinalIgnoreCase))
                {
                    DateTime startTime = DateTime.Now;
                    this.Logger.LogMessage($"[SEARCH-TIMING] Request received, starting to parse body. ContentLength={httpListenerRequest.ContentLength64}");
                    
                    byte[] searchFloppyBytes = ParseMultipartDataBytes(httpListenerRequest);
                    
                    this.Logger.LogMessage($"[SEARCH-TIMING] Body parsed in {(DateTime.Now - startTime).TotalMilliseconds:F0}ms, received {searchFloppyBytes.Length} bytes");

                    HttpSearchFloppyRequest searchRequest;
                    try
                    {
                        searchRequest = HttpSearchFloppyRequest.ParseFromBytes(searchFloppyBytes);
                    }
                    catch (ArgumentException ex)
                    {
                        this.Logger.LogMessage($"[SEARCH] Invalid request: {ex.Message}");
                        WriteResponse(httpListenerResponse, "ERROR: Invalid search request", null);
                        return;
                    }

                    string searchTerm = new string(searchRequest.SearchTerm, 0, searchRequest.SearchTermLength).TrimEnd();
                        
                    this.Logger.LogMessage($"[SEARCH] TERM={searchTerm} *** [SESSIONID] = {searchRequest.SessionId}");
                        
                    Session session = sessionManager.GetOrCreateSession(searchRequest.SessionId);

                    if ((searchTerm.StartsWith("+") || searchTerm.StartsWith("-")) && session.CachedSearchResults != null && session.CachedSearchResults.Length > 0)
                    {
                        HandleSearchPagination(httpListenerResponse, session, searchTerm);
                        return;
                    }

                    DateTime searchStart = DateTime.Now;
                    SearchFloppiesRequest searchFloppiesRequest = new SearchFloppiesRequest();
                    searchFloppiesRequest.Operation = 5;
                    searchFloppiesRequest.SearchTerm = searchTerm.ToArray();
                    searchFloppiesRequest.SearchTermLength = (byte)searchTerm.Length;

                    SearchFloppyResponse searchFloppyResponse = session.FloppyResolver.SearchFloppys(searchFloppiesRequest, out FloppyInfo[] foundFloppys);
                    
                    this.Logger.LogMessage($"[SEARCH-TIMING] Search completed in {(DateTime.Now - searchStart).TotalMilliseconds:F0}ms, found {foundFloppys?.Length ?? 0} results");
                    
                    if (searchFloppyResponse.ResultCount == 0)
                    {
                        session.CachedSearchResults = null;
                        session.LastSearchTerm = null;
                        session.CurrentSearchPage = 0;
                        
                        string payload = "\r\n" + string.Concat("NO RESULTS FOUND\r\n") + "\0";
                        WriteResponse(httpListenerResponse, payload, session);
                        this.Logger.LogMessage($"[SEARCH-TIMING] TOTAL request time: {(DateTime.Now - startTime).TotalMilliseconds:F0}ms");
                    }
                    else
                    {
                        session.CachedSearchResults = foundFloppys;
                        session.LastSearchTerm = searchTerm;
                        session.CurrentSearchPage = 0;
                        
                        DisplaySearchPage(httpListenerResponse, session, 0, startTime);
                    }

                    return;
                }

                // MOUNT
                if (httpListenerRequest.HttpMethod == "POST" && httpListenerRequest.Url.AbsolutePath.Equals("/mount", StringComparison.OrdinalIgnoreCase))
                {
                    byte[] mountBytes = ParseMultipartDataBytes(httpListenerRequest);

                    HttpMountRequest mountRequest;
                    try
                    {
                        mountRequest = HttpMountRequest.ParseFromBytes(mountBytes);
                    }
                    catch (ArgumentException ex)
                    {
                        this.Logger.LogMessage($"[MOUNT] Invalid request: {ex.Message}");
                        WriteResponse(httpListenerResponse, "ERROR: Invalid mount request", null);
                        return;
                    }

                    string imageIdOfFilename = mountRequest.GetImageIdOrFilenameString().TrimEnd();
                    
                    this.Logger.LogMessage($"[Mount] image={imageIdOfFilename}");

                    Session session = sessionManager.GetOrCreateSession(mountRequest.SessionId);

                    if (imageIdOfFilename.StartsWith("+") || imageIdOfFilename.StartsWith("-"))
                    {
                        HandleSearchPagination(httpListenerResponse, session, imageIdOfFilename);
                        return;
                    }

                    FloppyIdentifier floppyIdentifier;
                    ushort fullId;
                    if (imageIdOfFilename.Length <= 5 && int.TryParse(imageIdOfFilename, out int imageIdInt))
                    {
                        if (imageIdInt < 0 || imageIdInt > 65535)
                        {
                            WriteResponse(httpListenerResponse, "\r\nERROR: INVALID FLOPPY ID\0", session);
                            return;
                        }

                        fullId = (ushort)imageIdInt;
                        floppyIdentifier = new FloppyIdentifier
                        {
                            IdLo = (byte)(fullId & 0xFF),
                            IdHi = (byte)(fullId >> 8)
                        };
                        
                        this.Logger.LogMessage($"[Mount] Mounting by ID: {imageIdInt} (IdLo={floppyIdentifier.IdLo}, IdHi={floppyIdentifier.IdHi})");
                    }
                    else
                    {
                        floppyIdentifier = session.FloppyResolver.FindFloppyIdentifierByName(imageIdOfFilename);
                        if (floppyIdentifier.Equals(default(FloppyIdentifier)))
                        {
                            WriteResponse(httpListenerResponse, "\r\nERROR: FLOPPY NOT FOUND\0", session);
                            return;
                        }
                    }

                    FloppyInfo floppyInfo = session.FloppyResolver.InsertFloppy(floppyIdentifier);
                    string fileName = new string(floppyInfo.ImageName).TrimEnd('\0');
                    
                    fullId = (ushort)(floppyInfo.IdLo | (floppyInfo.IdHi << 8));

                    string message = $"\r\nFLOPPY INSERTED (ID={fullId} {fileName})";

                    if (fileName.ToLower().EndsWith("prg") ||
                        session.FloppyResolver.GetType() == typeof(HvscPsidFloppyResolver))
                    {
                        message += $"\r\n\r\nLOAD \"*\",8,1";
                    }
                    else
                    {
                        message += $"\r\n\r\nLOAD \"$\",8";
                    }

                    string payload = "\r\n" + string.Concat(message) + "\0";

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
                
                // Always try to close the response to prevent hanging
                try
                {
                    this.HttpListenerContext.Response?.Close();
                }
                catch { }
            }
        }

        private byte[] ParseMultipartDataBytes(HttpListenerRequest request)
        {
            if (request.ContentType?.Contains("multipart/form-data") != true)
            {
                return ReadRequestStream(request);
            }

            byte[] fullBody = ReadRequestStream(request);

            if (fullBody.Length == 0)
                return new byte[0];

            byte[] headerPattern = Encoding.ASCII.GetBytes("Content-Disposition: form-data; name=\"data\"");
            int headerStart = FindBytes(fullBody, headerPattern);

            if (headerStart < 0)
                return new byte[0];

            byte[] crlfcrlf = new byte[] { 13, 10, 13, 10 };
            int dataStart = FindBytes(fullBody, crlfcrlf, headerStart);
            if (dataStart < 0)
                return new byte[0];

            dataStart += 4;

            int dataEnd = fullBody.Length;
            
            for (int i = fullBody.Length - 1; i >= dataStart + 1; i--)
            {
                if (fullBody[i - 1] == 13 && fullBody[i] == 10)
                {
                    if (i + 1 < fullBody.Length && fullBody[i + 1] == 45 && i + 2 < fullBody.Length && fullBody[i + 2] == 45)
                    {
                        dataEnd = i - 1;
                        break;
                    }
                }
            }

            int length = dataEnd - dataStart;
            if (length <= 0)
                return new byte[0];

            byte[] result = new byte[length];
            Array.Copy(fullBody, dataStart, result, 0, length);
            
            return result;
        }

        private byte[] ReadRequestStream(HttpListenerRequest request)
        {
            try
            {
                // Validate payload size for C64 - reject anything unreasonably large
                if (request.ContentLength64 > MAX_EXPECTED_PAYLOAD_SIZE)
                {
                    this.Logger.LogMessage($"Content-Length {request.ContentLength64} exceeds max {MAX_EXPECTED_PAYLOAD_SIZE}, rejecting", VDRIVE_Contracts.Enums.LogSeverity.Warning);
                    return new byte[0];
                }

                if (request.ContentLength64 > 0)
                {
                    byte[] buffer = new byte[request.ContentLength64];
                    int totalRead = 0;
                    
                    using (var stream = request.InputStream)
                    {
                        // Use synchronous Read() - HttpListener already buffered the request
                        while (totalRead < buffer.Length)
                        {
                            int bytesRead = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                            
                            if (bytesRead == 0)
                            {
                                // Stream ended prematurely
                                this.Logger.LogMessage($"Stream ended after {totalRead}/{buffer.Length} bytes", VDRIVE_Contracts.Enums.LogSeverity.Warning);
                                break;
                            }
                            
                            totalRead += bytesRead;
                            this.Logger.LogMessage($"Read {bytesRead} bytes (total: {totalRead}/{buffer.Length})", VDRIVE_Contracts.Enums.LogSeverity.Verbose);
                        }
                    }
                    
                    byte[] result = totalRead == buffer.Length ? buffer : buffer.Take(totalRead).ToArray();
                    
                    if (totalRead > 0 && totalRead < buffer.Length)
                    {
                        this.Logger.LogMessage($"Incomplete read: Expected {buffer.Length} bytes, got {totalRead} bytes.", VDRIVE_Contracts.Enums.LogSeverity.Warning);
                    }
                    else if (totalRead == 0)
                    {
                        this.Logger.LogMessage($"No data read. Content-Length was {buffer.Length}. Client may have disconnected.", VDRIVE_Contracts.Enums.LogSeverity.Error);
                    }
                    
                    // Hex dump at verbose level
                    if (result.Length > 0 && result.Length <= 256)
                    {
                        this.Logger.LogMessage($"Request body ({result.Length} bytes):", VDRIVE_Contracts.Enums.LogSeverity.Verbose);
                        
                        for (int i = 0; i < result.Length; i += 16)
                        {
                            int rowLength = Math.Min(16, result.Length - i);
                            string hexPart = string.Join(" ", result.Skip(i).Take(rowLength).Select(b => b.ToString("X2")));
                            string asciiPart = string.Join("", result.Skip(i).Take(rowLength).Select(b => b >= 32 && b < 127 ? (char)b : '.'));
                            this.Logger.LogMessage($"  {i:X4}: {hexPart,-47} | {asciiPart}", VDRIVE_Contracts.Enums.LogSeverity.Verbose);
                        }
                    }
                    
                    return result;
                }
                else
                {
                    // No Content-Length - read until stream ends
                    using (var memoryStream = new MemoryStream())
                    using (var stream = request.InputStream)
                    {
                        byte[] buffer = new byte[4096];
                        int bytesRead;
                        
                        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            memoryStream.Write(buffer, 0, bytesRead);
                            
                            if (memoryStream.Length > MAX_EXPECTED_PAYLOAD_SIZE)
                            {
                                this.Logger.LogMessage($"Stream exceeds max size {MAX_EXPECTED_PAYLOAD_SIZE}", VDRIVE_Contracts.Enums.LogSeverity.Warning);
                                break;
                            }
                        }
                        
                        byte[] result = memoryStream.ToArray();
                        
                        if (result.Length > 0)
                        {
                            this.Logger.LogMessage($"Read {result.Length} bytes (no Content-Length)", VDRIVE_Contracts.Enums.LogSeverity.Verbose);
                        }
                        
                        return result;
                    }
                }
            }
            catch (IOException ex)
            {
                this.Logger.LogMessage($"IO error reading: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Error);
                return new byte[0];
            }
            catch (Exception ex)
            {
                this.Logger.LogMessage($"Error reading: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Error);
                return new byte[0];
            }
        }

        private static bool IsValidLoadAddress(ILogger logger, ushort dest_ptr_start, int end_dest_ptr)
        {
            List<ushort> rejectedLoadAddresses = new List<ushort>()
            {
                0x0314, // BASIC IRQ
                0x0316, // BASIC NMI
                0xFFFE, // KERNAL IRQ
                0xFFFA, // KERNAL NMI
                0xEA38, // LOAD address IRQ
                0xC000,  // VDRIVE location
                0xD000, // WiC64 registers
            };

            if (rejectedLoadAddresses.Any(r => r == dest_ptr_start))                
            {
                logger.LogMessage($"Invalid load address 0x{dest_ptr_start:X4}-0x{end_dest_ptr:X4}, rejecting", VDRIVE_Contracts.Enums.LogSeverity.Warning);
                return false;
            }

            if (end_dest_ptr >= 0xc032)
            {
                logger.LogMessage($"Load end 0x{end_dest_ptr:X4} overlaps VDRIVE memory", VDRIVE_Contracts.Enums.LogSeverity.Warning);
            }

            return true;
        }        

        private void WriteLoadResponse(HttpListenerContext httpListenerContext, byte[] filePayload, LoadResponse loadResponse, Session session)
        {
            try
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

                // Write response with timeout protection
                resp.ContentLength64 = fullResponse.Count;
                
                this.Logger.LogMessage($"[LOAD-WRITE] Writing {fullResponse.Count} bytes", VDRIVE_Contracts.Enums.LogSeverity.Verbose);
                
                var writeTask = resp.OutputStream.WriteAsync(fullResponse.ToArray(), 0, fullResponse.Count);
                
                // Timeout: 5 seconds base + 100ms per KB (for 50KB = 10 seconds total)
                int timeoutMs = 5000 + (fullResponse.Count / 1024 * 100);
                
                if (!writeTask.Wait(timeoutMs))
                {
                    this.Logger.LogMessage($"[LOAD-WRITE] Timeout after {timeoutMs}ms writing {fullResponse.Count} bytes", VDRIVE_Contracts.Enums.LogSeverity.Error);
                    throw new TimeoutException($"Write timeout after {timeoutMs}ms");
                }
                
                resp.OutputStream.Flush();
                resp.Close();
                
                this.Logger.LogMessage($"[LOAD-WRITE] Successfully wrote {fullResponse.Count} bytes", VDRIVE_Contracts.Enums.LogSeverity.Verbose);
            }
            catch (Exception ex)
            {
                this.Logger.LogMessage($"[LOAD-WRITE] Error: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Error);
                
                // Always try to close response to prevent hanging
                try { httpListenerContext.Response?.Close(); } catch { }
            }
        }

        private void WriteSaveResponse(HttpListenerResponse response, string text, SaveResponse saveResponse, Session session = null)
        {
            try
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

                var writeTask = response.OutputStream.WriteAsync(fullResponse.ToArray(), 0, fullResponse.Count);
                
                // 5 second timeout for small SAVE responses
                if (!writeTask.Wait(5000))
                {
                    this.Logger.LogMessage($"[SAVE-WRITE] Timeout after 5000ms", VDRIVE_Contracts.Enums.LogSeverity.Error);
                    throw new TimeoutException("Write timeout");
                }
                
                response.OutputStream.Flush();
                response.Close();
            }
            catch (Exception ex)
            {
                this.Logger.LogMessage($"[SAVE-WRITE] Error: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Error);
                
                try { response?.Close(); } catch { }
            }
        }

        private void WriteResponse(HttpListenerResponse response, string text, Session session = null)
        {
            try
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

                var writeTask = response.OutputStream.WriteAsync(fullResponse, 0, fullResponse.Length);
                
                // 5 second timeout for small text responses
                if (!writeTask.Wait(5000))
                {
                    this.Logger.LogMessage($"[WRITE] Timeout after 5000ms", VDRIVE_Contracts.Enums.LogSeverity.Error);
                    throw new TimeoutException("Write timeout");
                }
                
                response.OutputStream.Flush();
                response.Close();
            }
            catch (Exception ex)
            {
                this.Logger.LogMessage($"[WRITE] Error: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Error);
                
                try { response?.Close(); } catch { }
            }
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

        private void HandleSearchPagination(HttpListenerResponse response, Session session, string paginationCommand)
        {
            if (session.CachedSearchResults == null || session.CachedSearchResults.Length == 0)
            {
                WriteResponse(response, "\r\nERROR: NO SEARCH RESULTS TO PAGINATE\r\nPERFORM A SEARCH FIRST\0", session);
                return;
            }

            int pageSize = this.Configuration.SearchPageSize;
            int totalPages = (int)Math.Ceiling((double)session.CachedSearchResults.Length / pageSize);

            int pageOffset = 1;
            bool isForward = paginationCommand.StartsWith("+");
            
            string numericPart = paginationCommand.Substring(1).Trim();
            if (!string.IsNullOrEmpty(numericPart) && int.TryParse(numericPart, out int parsedOffset))
            {
                pageOffset = Math.Abs(parsedOffset);
            }

            int newPage = isForward 
                ? session.CurrentSearchPage + pageOffset 
                : session.CurrentSearchPage - pageOffset;

            newPage = Math.Max(0, Math.Min(newPage, totalPages - 1));

            this.Logger.LogMessage($"[PAGINATION] Command={paginationCommand}, CurrentPage={session.CurrentSearchPage}, NewPage={newPage}, TotalPages={totalPages}");

            DisplaySearchPage(response, session, newPage, null);
        }

        private void DisplaySearchPage(HttpListenerResponse response, Session session, int pageNumber, DateTime? startTime)
        {
            DateTime buildStart = DateTime.Now;
            
            int pageSize = this.Configuration.SearchPageSize;
            int totalResults = session.CachedSearchResults.Length;
            int totalPages = (int)Math.Ceiling((double)totalResults / pageSize);

            session.CurrentSearchPage = pageNumber;

            int startIndex = pageNumber * pageSize;
            int endIndex = Math.Min(startIndex + pageSize, totalResults);

            var pageResultsList = new List<string>();
            for (int i = startIndex; i < endIndex; i++)
            {
                var ff = session.CachedSearchResults[i];
                ushort fullId = (ushort)(ff.IdLo | (ff.IdHi << 8));
                pageResultsList.Add($"{fullId} {new string(ff.ImageName).TrimEnd('\0')}\r\n");
            }

            string fromMessage = pageNumber == 0 
                ? $"\r\n\r\n{this.Configuration.SearchIntroMessage.ToUpper()}\r\n\r\n{this.Configuration.FloppyResolver.ToUpper()} RESULTS: \"{session.LastSearchTerm}\"\r\n\r\n"
                : $"\r\n\r\n{this.Configuration.FloppyResolver.ToUpper()} RESULTS: \"{session.LastSearchTerm}\"\r\n\r\n";
            
            string pageInfo = $"\r\nPAGE {pageNumber + 1} OF {totalPages} ({totalResults} RESULTS)";
            string navInfo = "\r\n(+/- TO PAGE, ID TO MOUNT)";
            string payload = fromMessage + string.Concat(pageResultsList) + pageInfo + navInfo + "\0";

            const int maxPayloadSize = 512 - 2;
            int originalCount = pageResultsList.Count;
            while (Encoding.ASCII.GetByteCount(payload) > maxPayloadSize && pageResultsList.Count > 0)
            {
                pageResultsList.RemoveAt(pageResultsList.Count - 1);
                payload = fromMessage + string.Concat(pageResultsList) + pageInfo + navInfo + "\0";
            }

            DateTime sendStart = DateTime.Now;
            WriteResponse(response, payload, session);
            
            if (startTime.HasValue)
            {
                this.Logger.LogMessage($"[SEARCH-TIMING] Response built in {(DateTime.Now - buildStart).TotalMilliseconds:F0}ms");
            }
            
            if (pageResultsList.Count < originalCount)
            {
                this.Logger.LogMessage($"[SEARCH] Payload truncated from {originalCount} to {pageResultsList.Count} results");
            }
            
            if (startTime.HasValue)
            {
                this.Logger.LogMessage($"[SEARCH-TIMING] Response sent in {(DateTime.Now - sendStart).TotalMilliseconds:F0}ms");
                this.Logger.LogMessage($"[SEARCH-TIMING] TOTAL request time: {(DateTime.Now - startTime.Value).TotalMilliseconds:F0}ms");
            }
            
            for (int i = startIndex; i < endIndex && (i - startIndex) < pageResultsList.Count; i++)
            {
                var ff = session.CachedSearchResults[i];
                ushort fullId = (ushort)(ff.IdLo | (ff.IdHi << 8));
                this.Logger.LogMessage($"[DISPLAY] Page={pageNumber}, Index={i}, FullId={fullId}, Name={new string(ff.ImageName).TrimEnd('\0')}", VDRIVE_Contracts.Enums.LogSeverity.Verbose);
            }
        }
    }
}
