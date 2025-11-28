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
                
                string apiKey, basePath;                
                this.ExtractUrlParts(httpListenerRequest, out apiKey, out basePath);

                this.Logger.LogMessage($"{httpListenerRequest.HttpMethod} {httpListenerRequest.Url}");

                // LOAD
                if (httpListenerRequest.HttpMethod == "POST" && basePath.Equals("/load", StringComparison.OrdinalIgnoreCase))
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
                if (httpListenerRequest.HttpMethod == "POST" && basePath.Equals("/save", StringComparison.OrdinalIgnoreCase))
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
                if (httpListenerRequest.HttpMethod == "POST" && basePath.Equals("/search", StringComparison.OrdinalIgnoreCase))
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
                        WriteSearchResponse(httpListenerResponse, "ERROR: Invalid search request", null);
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
                        WriteSearchResponse(httpListenerResponse, payload, session);
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
                if (httpListenerRequest.HttpMethod == "POST" && basePath.Equals("/mount", StringComparison.OrdinalIgnoreCase))
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

        private void ExtractUrlParts(HttpListenerRequest httpListenerRequest, out string apiKey, out string basePath)
        {
            // Allow optional API key segment in the URL: e.g. /search/SOMEKEY or /search
            apiKey = null;
            basePath = httpListenerRequest.Url.AbsolutePath ?? "/";
            try
            {
                string trimmed = httpListenerRequest.Url.AbsolutePath?.Trim('/') ?? string.Empty;
                if (!string.IsNullOrEmpty(trimmed))
                {
                    string[] parts = trimmed.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 1)
                        basePath = "/" + parts[0].ToLowerInvariant();
                    if (parts.Length >= 2)
                        apiKey = parts[1];
                }
            }
            catch
            {
                // fallback to raw path if anything goes wrong
                basePath = httpListenerRequest.Url.AbsolutePath ?? "/";
            }
        }

        private byte[] ParseMultipartDataBytes(HttpListenerRequest request)
        {
            // For non-multipart, just read the stream directly
            if (request.ContentType?.Contains("multipart/form-data") != true)
            {
                return ReadRequestStream(request);
            }

            byte[] fullBody = ReadRequestStream(request);

            if (fullBody.Length == 0)
                return new byte[0];

            // Convert to string for easier parsing (it's all ASCII/UTF-8 anyway)
            string bodyText = Encoding.ASCII.GetString(fullBody);

            // Find the data field - format is:
            // Content-Disposition: form-data; name="data"
            // \r\n\r\n
            // <actual data bytes>
            // \r\n--boundary--

            int dataHeaderIndex = bodyText.IndexOf("Content-Disposition: form-data; name=\"data\"", StringComparison.OrdinalIgnoreCase);
            if (dataHeaderIndex < 0)
                return new byte[0];

            // Find the \r\n\r\n after the Content-Disposition line
            int dataStartIndex = bodyText.IndexOf("\r\n\r\n", dataHeaderIndex);
            if (dataStartIndex < 0)
                return new byte[0];

            dataStartIndex += 4; // Skip past the \r\n\r\n

            // Find the next boundary marker (starts with \r\n--)
            int dataEndIndex = bodyText.IndexOf("\r\n--", dataStartIndex);
            if (dataEndIndex < 0)
                dataEndIndex = fullBody.Length; // No trailing boundary, use end of body

            // Extract the data bytes
            int dataLength = dataEndIndex - dataStartIndex;
            if (dataLength <= 0)
                return new byte[0];

            byte[] result = new byte[dataLength];
            Array.Copy(fullBody, dataStartIndex, result, 0, dataLength);

            return result;
        }

        private byte[] ReadRequestStream(HttpListenerRequest request)
        {
            DateTime readStart = DateTime.Now;

            try
            {
                if (request.ContentLength64 > MAX_EXPECTED_PAYLOAD_SIZE)
                {
                    this.Logger.LogMessage($"Content-Length {request.ContentLength64} exceeds max {MAX_EXPECTED_PAYLOAD_SIZE}, rejecting", VDRIVE_Contracts.Enums.LogSeverity.Warning);
                    return new byte[0];
                }

                if (request.ContentLength64 > 0)
                {
                    this.Logger.LogMessage($"[READ-START] ContentLength={request.ContentLength64}, HasEntityBody={request.HasEntityBody}, RemoteEndPoint={request.RemoteEndPoint}", VDRIVE_Contracts.Enums.LogSeverity.Verbose);

                    byte[] buffer = new byte[request.ContentLength64];
                    int totalRead = 0;
                    int readAttempts = 0;

                    int timeoutSeconds = this.Configuration.ReceiveTimeoutSeconds ?? 10;
                    DateTime absoluteTimeout = readStart.AddSeconds(timeoutSeconds);

                    using (var stream = request.InputStream)
                    {
                        while (totalRead < buffer.Length)
                        {
                            // Check absolute timeout
                            double elapsedSeconds = (DateTime.Now - readStart).TotalSeconds;
                            if (DateTime.Now > absoluteTimeout)
                            {
                                this.Logger.LogMessage($"[READ-TIMEOUT] Absolute timeout of {timeoutSeconds}s exceeded at {totalRead}/{buffer.Length} bytes after {elapsedSeconds:F1}s", VDRIVE_Contracts.Enums.LogSeverity.Error);
                                break;
                            }

                            readAttempts++;
                            DateTime readIterationStart = DateTime.Now;

                            // Try async read with 5-second per-iteration timeout
                            var readTask = stream.ReadAsync(buffer, totalRead, buffer.Length - totalRead);

                            if (!readTask.Wait(5000))
                            {
                                double iterationSeconds = (DateTime.Now - readIterationStart).TotalSeconds;
                                this.Logger.LogMessage($"[READ-ITERATION-TIMEOUT] Attempt #{readAttempts} timed out after {iterationSeconds:F1}s at {totalRead}/{buffer.Length} bytes", VDRIVE_Contracts.Enums.LogSeverity.Warning);

                                // If no data received after 3 attempts (15 seconds), give up
                                if (readAttempts >= 3 && totalRead == 0)
                                {
                                    this.Logger.LogMessage($"[READ-ABANDONED] No data after {readAttempts} attempts and {elapsedSeconds:F1}s - C64 likely hung", VDRIVE_Contracts.Enums.LogSeverity.Error);
                                    break;
                                }

                                continue; // Try again
                            }

                            int bytesRead = readTask.Result;
                            double readMs = (DateTime.Now - readIterationStart).TotalMilliseconds;

                            if (bytesRead == 0)
                            {
                                this.Logger.LogMessage($"[READ-ZERO] Attempt #{readAttempts}: Stream closed at {totalRead}/{buffer.Length} after {readMs:F1}ms (elapsed: {elapsedSeconds:F1}s)", VDRIVE_Contracts.Enums.LogSeverity.Warning);
                                break;
                            }

                            totalRead += bytesRead;

                            if (readMs > 100 || bytesRead < 1024)
                            {
                                this.Logger.LogMessage($"[READ] Attempt #{readAttempts}: {bytesRead} bytes in {readMs:F1}ms (total: {totalRead}/{buffer.Length}, elapsed: {elapsedSeconds:F1}s)", VDRIVE_Contracts.Enums.LogSeverity.Verbose);
                            }
                        }
                    }

                    double totalMs = (DateTime.Now - readStart).TotalMilliseconds;

                    if (totalRead < buffer.Length)
                    {
                        this.Logger.LogMessage($"[READ-INCOMPLETE] Expected {buffer.Length}, got {totalRead} in {totalMs:F1}ms after {readAttempts} attempts - CLIENT DID NOT SEND BODY", VDRIVE_Contracts.Enums.LogSeverity.Error);
                    }
                    else
                    {
                        this.Logger.LogMessage($"[READ-COMPLETE] Read {totalRead} bytes in {totalMs:F1}ms ({readAttempts} attempts, {(totalRead / totalMs):F1} KB/s)", VDRIVE_Contracts.Enums.LogSeverity.Verbose);
                    }

                    return totalRead > 0 ? buffer.Take(totalRead).ToArray() : new byte[0];
                }
                else
                {
                    this.Logger.LogMessage($"[READ-NO-LENGTH] No Content-Length header", VDRIVE_Contracts.Enums.LogSeverity.Verbose);
                    return new byte[0];
                }
            }
            catch (Exception ex)
            {
                double totalMs = (DateTime.Now - readStart).TotalMilliseconds;
                this.Logger.LogMessage($"[READ-ERROR] After {totalMs:F1}ms: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Error);
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

        private void WriteSearchResponse(HttpListenerResponse response, string text, Session session = null)
        {
            try
            {
                // Create HTTP response header with result count
                ushort resultCount = session?.CachedSearchResults != null ? (ushort)session.CachedSearchResults.Length : (ushort)0;
                HttpSearchResponse httpResponse = HttpSearchResponse.Create(
                    session?.SessionId ?? 0,
                    resultCount
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

                this.Logger.LogMessage($"[SEARCH-WRITE] Writing {fullResponse.Count} bytes (header: 4 bytes, SessionId={session?.SessionId ?? 0}, ResultCount={resultCount})", VDRIVE_Contracts.Enums.LogSeverity.Verbose);

                var writeTask = response.OutputStream.WriteAsync(fullResponse.ToArray(), 0, fullResponse.Count);

                // 5 second timeout for search responses
                if (!writeTask.Wait(5000))
                {
                    this.Logger.LogMessage($"[SEARCH-WRITE] Timeout after 5000ms", VDRIVE_Contracts.Enums.LogSeverity.Error);
                    throw new TimeoutException("Write timeout");
                }

                response.OutputStream.Flush();
                response.Close();
            }
            catch (Exception ex)
            {
                this.Logger.LogMessage($"[SEARCH-WRITE] Error: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Error);

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

        private void HandleSearchPagination(HttpListenerResponse response, Session session, string paginationCommand)
        {
            if (session.CachedSearchResults == null || session.CachedSearchResults.Length == 0)
            {
                WriteSearchResponse(response, "\r\nERROR: NO SEARCH RESULTS TO PAGINATE\r\nPERFORM A SEARCH FIRST\0", session);
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

            string pageInfo = $"\r\n{pageNumber + 1} OF {totalPages} ({totalResults} RESULTS)";
            string navInfo = "\r\n(+/- TO PAGE, # TO MOUNT)";
            string payload = fromMessage + string.Concat(pageResultsList) + pageInfo + navInfo + "\0";

            const int maxPayloadSize = 512 - 10; // FIXME: needs to subtract just the header length
            int originalCount = pageResultsList.Count;
            while (Encoding.ASCII.GetByteCount(payload) > maxPayloadSize && pageResultsList.Count > 0)
            {
                pageResultsList.RemoveAt(pageResultsList.Count - 1);
                payload = fromMessage + string.Concat(pageResultsList) + pageInfo + navInfo + "\0";
            }

            DateTime sendStart = DateTime.Now;
            WriteSearchResponse(response, payload, session);

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
