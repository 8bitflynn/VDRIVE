using System.Net;
using System.Text;
using VDRIVE.Floppy.Impl;
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
                HttpListenerRequest httpListnerRequest = this.HttpListenerContext.Request;
                HttpListenerResponse httpListenerResponse = this.HttpListenerContext.Response;

                httpListenerResponse.SendChunked = false;
                httpListenerResponse.KeepAlive = true;

                this.Logger.LogMessage($"{httpListnerRequest.HttpMethod} {httpListnerRequest.Url}");

                // LOAD
                if (httpListnerRequest.HttpMethod == "POST" && httpListnerRequest.Url.AbsolutePath.Equals("/load", StringComparison.OrdinalIgnoreCase))
                {
                    string fileName = ParseMultipartData(httpListnerRequest);

                    DateTime start = DateTime.Now;

                    // Parse the multipart data to extract just the "data" field content as bytes
                    byte[] data = ParseMultipartDataBytes(httpListnerRequest);

                    this.Logger.LogMessage($"Loading '{fileName}' (length={fileName.Length}) starting");

                    byte[] fullFile = null;
                    LoadRequest loadRequest = new LoadRequest();
                    loadRequest.Operation = 3;
                    loadRequest.FileName = fileName.ToArray();
                    loadRequest.FileNameLength = (byte)fileName.Length;
                    storageAdapter.Load(loadRequest, floppyResolver, out fullFile);

                    ushort dest_ptr_start = 0x00;
                    if (fullFile != null)
                    {
                        byte[] dest_ptr_bytes = fullFile.Take(2).ToArray();
                        dest_ptr_start = (ushort)(fullFile[0] | (fullFile[1] << 8));
                        this.Logger.LogMessage($"Start Address: 0x{dest_ptr_start:X4}");

                        int endAddress = dest_ptr_start + fullFile.Length - 1;
                        this.Logger.LogMessage($"End Address: 0x{endAddress:X4}");
                    }

                    WritePayloadResponse(this.HttpListenerContext, fullFile);

                    if (fullFile != null)
                    {
                        DateTime end = DateTime.Now;
                        Logger.LogMessage($"TimeTook:{((end - start).TotalMilliseconds / 1000).ToString("F3")} " +
                                         $"BytesPerSec:{(fullFile.Length / (end - start).TotalMilliseconds * 1000).ToString("F3")}");
                    }

                    fullFile = null;
                    return;
                }

                // SAVE (receives single POST with combined payload: [length][filename][prg header][data])
                if (httpListnerRequest.HttpMethod == "POST" && httpListnerRequest.Url.AbsolutePath.Equals("/save", StringComparison.OrdinalIgnoreCase))
                {
                    // Parse the multipart data to extract just the "data" field content as bytes
                    byte[] data = ParseMultipartDataBytes(httpListnerRequest);

                    this.Logger.LogMessage($"[Save Debug] data.Length={data.Length}, First bytes: {BitConverter.ToString(data.Take(20).ToArray())}");

                    if (data == null || data.Length < 2)
                    {
                        WriteResponse(httpListenerResponse, "ERROR: Invalid payload");
                        return;
                    }

                    // Parse combined payload: [length_byte][filename...][file_data...]
                    int filenameLength = data[0];

                    if (data.Length < 1 + filenameLength)
                    {
                        WriteResponse(httpListenerResponse, "ERROR: Invalid filename length");
                        return;
                    }

                    // Extract filename
                    string fileName = Encoding.ASCII.GetString(data, 1, filenameLength).TrimEnd();
                    this.Logger.LogMessage($"[Save] Filename: {fileName}");

                    // Extract file data (everything after filename)
                    int fileDataOffset = 1 + filenameLength;
                    int fileDataLength = data.Length - fileDataOffset;
                    byte[] fileData = new byte[fileDataLength];
                    Array.Copy(data, fileDataOffset, fileData, 0, fileDataLength);

                    this.Logger.LogMessage($"[Save] File data: {fileDataLength} bytes");

                    SaveRequest saveRequest = new SaveRequest();
                    saveRequest.Operation = 4;
                    saveRequest.FileName = fileName.ToArray();
                    saveRequest.FileNameLength = (byte)fileName.Length;
                    saveRequest.DeviceNum = 8; // defaulting for now
                    saveRequest.SecondaryAddr = 1; // defaulting for now
                    saveRequest.TargetAddressLo = fileData[0];
                    saveRequest.TargetAddressHi = fileData[1];
                    byte[] payload = fileData.Skip(2).ToArray();

                    Logger.LogMessage($"Save Request: SAVE\"{new string(saveRequest.FileName)}\",{saveRequest.DeviceNum}{(saveRequest.SecondaryAddr != 0 ? "," + saveRequest.SecondaryAddr : "")}");

                    SaveResponse saveResponse = storageAdapter.Save(saveRequest, floppyResolver, payload);

                    // TODO: return proper error codes
                    string payloadResponse = "\r\n" + string.Concat("SAVE OK") + "\r\n" + "\0";

                    WriteResponse(httpListenerResponse, payloadResponse);
                    return;
                }

                // SEARCH
                if (httpListnerRequest.HttpMethod == "POST" && httpListnerRequest.Url.AbsolutePath.Equals("/search", StringComparison.OrdinalIgnoreCase))
                {
                    string searchTerm = ParseMultipartData(httpListnerRequest);

                    this.Logger.LogMessage($"[SEARCH] TERM={searchTerm}");

                    SearchFloppiesRequest searchFloppiesRequest = new SearchFloppiesRequest();
                    searchFloppiesRequest.Operation = 5;
                    searchFloppiesRequest.SearchTerm = searchTerm.ToArray();
                    searchFloppiesRequest.SearchTermLength = (byte)searchTerm.Length;

                    SearchFloppyResponse searchFloppyResponse = floppyResolver.SearchFloppys(searchFloppiesRequest, out FloppyInfo[] foundFloppys);
                    if (searchFloppyResponse.ResultCount == 0)
                    {
                        string payload = "\r\n" + string.Concat("NO RESULTS FOUND\r\n") + "\0";
                        WriteResponse(httpListenerResponse, payload);
                    }
                    else
                    {
                        var results = foundFloppys.Select(ff => $"{ff.IdLo} {new string(ff.ImageName).TrimEnd('\0')}\r\n");
                        string payload = "\r\n" + string.Concat(results) + "\0";

                        int length = Encoding.ASCII.GetByteCount(payload);
                        //payload = payload.Substring(0, 300) + "\0";
                        WriteResponse(httpListenerResponse, payload);

                        // Write the response with length prefix
                        //httpListenerResponse.ContentLength64 = response.Length;
                        //httpListenerResponse.OutputStream.Write(response, 0, response.Length);
                        //httpListenerResponse.OutputStream.Close();
                    }

                    return;
                }

                // MOUNT
                if (httpListnerRequest.HttpMethod == "POST" && httpListnerRequest.Url.AbsolutePath.Equals("/mount", StringComparison.OrdinalIgnoreCase))
                {
                    string imageId = ParseMultipartData(httpListnerRequest);

                    this.Logger.LogMessage($"[Mount] image={imageId}");

                    FloppyIdentifier floppyIdentifier = new FloppyIdentifier // right now only showing single page of results
                    {
                        IdLo = byte.Parse(imageId),
                        IdHi = 0
                    };

                    FloppyInfo floppyInfo = floppyResolver.InsertFloppy(floppyIdentifier);
                    string fileName = new string(floppyInfo.ImageName).TrimEnd('\0');

                    string message = $"MOUNT OK (ID={imageId} {fileName})\r\n";

                    // hack: tacking on typical load commands
                    // to save some typing on C64
                    if (fileName.ToLower().EndsWith(".prg") ||
                        floppyResolver.GetType() == typeof(HvscPsidFloppyResolver))
                    {
                        message += $"\r\nLOAD \"*\",8,1";
                    }
                    else
                    {
                        message += $"\r\nLOAD \"$\",8,1";
                    }

                    string payload = "\r\n" + string.Concat(message) + "\r\n" + "\0";

                    WriteResponse(httpListenerResponse, payload);

                    return;
                }               

                // --- Default 404 ---
                httpListenerResponse.StatusCode = 404;
                WriteResponse(httpListenerResponse, "Not Found");
            }
            catch (Exception ex)
            {
                this.Logger.LogMessage($"[Error] {ex.Message}");
            }
        }

        private string ParseMultipartData(HttpListenerRequest request)
        {
            if (request.ContentType?.Contains("multipart/form-data") != true)
            {
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                    return reader.ReadToEnd().Trim();
            }

            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                string fullBody = reader.ReadToEnd();

                // Match: Content-Disposition header with name="data", followed by blank line, then capture everything until boundary
                var match = System.Text.RegularExpressions.Regex.Match(
                    fullBody,
                    @"Content-Disposition:\s*form-data;\s*name=""data""[\r\n]+[\r\n]+(.*?)[\r\n]+--",
                    System.Text.RegularExpressions.RegexOptions.Singleline
                );

                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }

            return "";
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

            string boundary = "--" + request.ContentType.Split(new[] { "boundary=" }, StringSplitOptions.None)[1];
            byte[] boundaryBytes = Encoding.ASCII.GetBytes(boundary);

            // Read entire body as bytes
            byte[] fullBody;
            using (var memoryStream = new MemoryStream())
            {
                request.InputStream.CopyTo(memoryStream);
                fullBody = memoryStream.ToArray();
            }

            // Find the "data" field by searching for the header pattern in bytes
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

            // Find the next boundary
            int dataEnd = FindBytes(fullBody, boundaryBytes, dataStart);
            if (dataEnd < 0)
                dataEnd = fullBody.Length;

            // Back up over any trailing CRLF before the boundary
            while (dataEnd > dataStart && (fullBody[dataEnd - 1] == 10 || fullBody[dataEnd - 1] == 13))
                dataEnd--;

            // Extract the data
            int length = dataEnd - dataStart;
            if (length <= 0)
                return new byte[0];

            byte[] result = new byte[length];
            Array.Copy(fullBody, dataStart, result, 0, length);
            return result;
        }

        private int FindBytes(byte[] haystack, byte[] needle, int startIndex = 0)
        {
            for (int i = startIndex; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return i;
            }
            return -1;
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

        async Task WritePayloadResponse(HttpListenerContext httpListenerContext, byte[] payload)
        {
            var resp = httpListenerContext.Response;
            resp.SendChunked = false;
            resp.ContentLength64 = payload == null ? 0 : payload.Length;
            await resp.OutputStream.WriteAsync(payload, 0, payload == null ? 0 : payload.Length);
            await resp.OutputStream.FlushAsync();
            resp.Close();
        }

        private async Task WriteResponse(HttpListenerResponse response, string text)
        {
            byte[] msg = Encoding.ASCII.GetBytes(text); // mimic null-terminated strings
            response.StatusCode = 200;
            response.ContentType = "text/plain";
            response.ContentLength64 = msg.Length;
            await response.OutputStream.WriteAsync(msg, 0, msg.Length);
            response.OutputStream.Flush();
            response.Close();
        }
    }
}
