using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using VDRIVE.Floppy;
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
        private const int MAX_IMAGE_SIZE = 4 * 1024 * 1024; // /image: a D64 is ~170 KB, a multi-disk ZIP a few hundred KB
        private const int MAX_DISKS_FOR_BROWSER = 12;       // the browser emulator has a fixed 16 MB; ~20 unpacked disks crash it (OOM)
        private static readonly string[] WholeImageExtensions = { ".d64", ".g64", ".zip" };

        // TEMPORARY: a real C64 can't use ZIP disk sets or G64 images (only the website's emulator can),
        // so hide them from C64 search results. The website's proxy marks its requests with this header.
        // Remove when the C64 can copy whole disks to an SD card / drive.
        private const string WebClientHeader = "X-VDrive-Client";
        private static readonly string[] WebOnlyExtensions = { ".zip", ".g64" };
        
        public HttpClientProtocolHandler(IConfiguration configuration, ILogger logger, HttpListenerContext httpListenerContext)
        {
            this.Configuration = configuration;
            this.Logger = logger;
            this.HttpListenerContext = httpListenerContext;
        }
        private IConfiguration Configuration;
        private ILogger Logger;
        private HttpListenerContext HttpListenerContext;

        public void HandleClient(ISessionProvider sessionProvider)
        {
            try
            {
                HttpListenerRequest httpListenerRequest = this.HttpListenerContext.Request;
                HttpListenerResponse httpListenerResponse = this.HttpListenerContext.Response;

                httpListenerResponse.SendChunked = false;
                httpListenerResponse.KeepAlive = true;
                
                string clientToken, basePath;                
                this.ExtractUrlParts(httpListenerRequest, out clientToken, out basePath);

                // Resolve client addresses (always include TCP remote endpoint; include X-Forwarded-For if present)
                string xffHeader = httpListenerRequest.Headers["X-Forwarded-For"];
                string tcpRemote = httpListenerRequest.RemoteEndPoint?.ToString() ?? httpListenerRequest.UserHostAddress ?? "unknown";

                // If XFF contains a list, take the left-most non-empty entry as the original client IP
                string xffFirst = null;
                if (!string.IsNullOrEmpty(xffHeader))
                {
                    xffFirst = xffHeader.Split(',').Select(s => s.Trim()).FirstOrDefault(s => !string.IsNullOrEmpty(s));
                }

                // Build a compact logging representation: keep TCP peer (always) and show original client from XFF when available
                string remoteAddr = tcpRemote;
                if (!string.IsNullOrEmpty(xffFirst))
                {
                    // include both a short and the full header for diagnostics
                    remoteAddr = $"{tcpRemote} via XFF={xffFirst}";
                }
                if (!string.IsNullOrEmpty(xffHeader) && xffHeader != xffFirst)
                {
                    // optionally append the full XFF for full chain visibility
                    remoteAddr += $" (XFF-full={xffHeader})";
                }

                // Single entry log including remote addresses
                this.Logger.LogMessage($"[{remoteAddr}] {httpListenerRequest.HttpMethod} {httpListenerRequest.Url}");

                // If any tokens are configured, require valid token
                if (this.Configuration.AllowedAuthTokens.Any()) 
                {
                    bool tokenValid = this.Configuration.AllowedAuthTokens.Any(allowedToken => allowedToken == clientToken);
                    if (!tokenValid)
                    {
                        this.Logger.LogMessage($"[401] Unauthorized access attempt with token '{clientToken}'", VDRIVE_Contracts.Enums.LogSeverity.Warning);
                        httpListenerResponse.StatusCode = 401;
                        WriteResponse(httpListenerResponse, "Unauthorized");
                        return;
                    }
                }                    

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
                        WriteLoadResponse(this.HttpListenerContext, new byte[0], errorResponse, sessionProvider.GetOrCreateSession(0));
                        return;
                    }

                    string fileName = loadRequest.GetFilenameString().TrimEnd();
                    Session session = sessionProvider.GetOrCreateSession(loadRequest.SessionId);

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

                    Session session = sessionProvider.GetOrCreateSession(saveRequest.SessionId);

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

                    Session session = sessionProvider.GetOrCreateSession(searchRequest.SessionId);

                    // Handle paging 
                    if ((searchTerm.StartsWith("+") || searchTerm.StartsWith("-")) && session.CachedSearchResults != null && session.CachedSearchResults.Length > 0)
                    {
                        HandleSearchPagination(httpListenerResponse, session, searchTerm);
                        return;
                    }

                    // switch this session's floppy resolver ("source"): @LOCAL, @CS, @HVSC, @C64
                    // "@HVSC COMMANDO" switches and searches in one go; other sessions are not affected
                    if (searchTerm.StartsWith("@") && !searchTerm.StartsWith("@NEW", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] sourceParts = searchTerm.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                        string resolverType = GetResolverTypeForCommand(sourceParts[0]);

                        if (resolverType == null)
                        {
                            WriteSearchResponse(httpListenerResponse, "\r\nUNKNOWN SOURCE\r\n\r\nSOURCES: @LOCAL @CS @HVSC @C64\r\n\0", session);
                            return;
                        }

                        session.FloppyResolver = FloppyResolverFactory.CreateFloppyResolver(resolverType, this.Configuration, this.Logger, session.ProcessRunner);
                        session.CachedSearchResults = null;
                        session.LastSearchTerm = null;
                        session.CurrentSearchPage = 0;

                        this.Logger.LogMessage($"[SEARCH] Session {session.SessionId} switched source to {resolverType}");

                        if (sourceParts.Length < 2 || string.IsNullOrWhiteSpace(sourceParts[1]))
                        {
                            WriteSearchResponse(httpListenerResponse, $"\r\nSOURCE: {GetSourceName(session)}\r\n\0", session);
                            return;
                        }

                        searchTerm = sourceParts[1].Trim(); // carry on and search the new source
                    }

                    // @NEW NAME[.D64|.D81|.G64] [DISK TITLE] - create an empty disk (DirMaster or VICE's c1541, whichever
                    // storage adapter is configured), left unmounted. Writes a file on this PC, so:
                    //  - not when StorageAdapterSettings.Readonly is on (same rule as SAVE)
                    //  - not from the website (its browser emulator can't use a disk created here)
                    if (searchTerm.StartsWith("@NEW", StringComparison.OrdinalIgnoreCase))
                    {
                        if (this.Configuration.StorageAdapterSettings?.Readonly == true)
                        {
                            this.Logger.LogMessage("[NEW] Refused: storage is read-only", VDRIVE_Contracts.Enums.LogSeverity.Warning);
                            WriteSearchResponse(httpListenerResponse, "\r\n\r\nREAD ONLY - NEW DISKS ARE TURNED OFF\r\n\0", session);
                            return;
                        }

                        if (string.Equals(httpListenerRequest.Headers[WebClientHeader], "web", StringComparison.OrdinalIgnoreCase))
                        {
                            WriteSearchResponse(httpListenerResponse, "\r\n\r\nNEW DISKS CAN ONLY BE MADE FROM A C64\r\n\0", session);
                            return;
                        }

                        string[] parts = searchTerm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 2)
                        {
                            WriteSearchResponse(httpListenerResponse, "\r\n\r\nUSAGE: @NEW NAME.D64 (OR .D81 .G64)\r\n\0", session);
                            return;
                        }

                        string nameAndType = parts[1];
                        int dot = nameAndType.LastIndexOf('.');
                        string floppyName = dot > 0 ? nameAndType.Substring(0, dot) : nameAndType;
                        string mediaType = dot > 0 ? nameAndType.Substring(dot + 1).ToUpperInvariant() : "D64";
                        if (mediaType != "D64" && mediaType != "D81" && mediaType != "G64")
                        {
                            WriteSearchResponse(httpListenerResponse, "\r\n\r\nUNKNOWN DISK TYPE - USE .D64 .D81 OR .G64\r\n\0", session);
                            return;
                        }

                        CreateFloppyRequest createFloppyRequest = new CreateFloppyRequest();
                        createFloppyRequest.Filename = floppyName;
                        createFloppyRequest.MediaType = mediaType;
                        createFloppyRequest.InternalName = parts.Length >= 3 ? string.Join(" ", parts.Skip(2)) : "";   // optional disk title
                        CreateFloppyResponse createFloppyResponse = session.StorageAdapter.CreateFloppyImage(createFloppyRequest);

                        string payload = createFloppyResponse != null && createFloppyResponse.Success
                            ? $"\r\n\r\nNEW FLOPPY CREATED - {floppyName}.{mediaType}\r\n\0"
                            : $"\r\n\r\nCOULD NOT CREATE DISK - {(createFloppyResponse?.ErrorMessage ?? "UNKNOWN ERROR").ToUpperInvariant()}\r\n\0";
                        WriteSearchResponse(httpListenerResponse, payload, session);
                        return;
                    }

                    DateTime searchStart = DateTime.Now;
                    SearchFloppiesRequest searchFloppiesRequest = new SearchFloppiesRequest();
                    searchFloppiesRequest.Operation = 5;
                    searchFloppiesRequest.SearchTerm = searchTerm.ToArray();
                    searchFloppiesRequest.SearchTermLength = (byte)searchTerm.Length;

                    SearchFloppyResponse searchFloppyResponse = session.FloppyResolver.SearchFloppys(searchFloppiesRequest, out FloppyInfo[] foundFloppys);

                    this.Logger.LogMessage($"[SEARCH-TIMING] Search completed in {(DateTime.Now - searchStart).TotalMilliseconds:F0}ms, found {foundFloppys?.Length ?? 0} results");

                    // TEMPORARY (see WebOnlyExtensions): the C64 only sees what it can load. IDs are kept,
                    // so the remaining results still mount by the number shown.
                    bool isWebClient = string.Equals(httpListenerRequest.Headers[WebClientHeader], "web", StringComparison.OrdinalIgnoreCase);
                    if (!isWebClient && foundFloppys != null)
                    {
                        int before = foundFloppys.Length;
                        foundFloppys = foundFloppys
                            .Where(ff => !WebOnlyExtensions.Any(ext => new string(ff.ImageName ?? new char[0]).TrimEnd('\0').EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                            .ToArray();
                        if (foundFloppys.Length != before)
                            this.Logger.LogMessage($"[SEARCH] Hid {before - foundFloppys.Length} ZIP/G64 result(s) from a C64 client");
                    }

                    if (searchFloppyResponse.ResultCount == 0 || foundFloppys == null || foundFloppys.Length == 0)
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

                    string imageIdOrFilename = mountRequest.GetImageIdOrFilenameString().TrimEnd();

                    this.Logger.LogMessage($"[Mount] image={imageIdOrFilename}");

                    Session session = sessionProvider.GetOrCreateSession(mountRequest.SessionId);                 

                    FloppyIdentifier floppyIdentifier;
                    ushort fullId;
                    if (imageIdOrFilename.Length <= 5 && int.TryParse(imageIdOrFilename, out int imageIdInt))
                    {
                        if (imageIdInt < 0 || imageIdInt > this.Configuration.MaxSearchResults)
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
                        floppyIdentifier = session.FloppyResolver.FindFloppyIdentifierByName(imageIdOrFilename);
                        if (floppyIdentifier.Equals(default(FloppyIdentifier)))
                        {
                            WriteResponse(httpListenerResponse, "\r\nERROR: FLOPPY NOT FOUND\0", session);
                            return;
                        }
                    }

                    FloppyInfo floppyInfo = session.FloppyResolver.InsertFloppy(floppyIdentifier);
                    string fileName = new string(floppyInfo.ImageName).TrimEnd('\0');

                    fullId = (ushort)(floppyInfo.IdLo | (floppyInfo.IdHi << 8));

                    // ZIP disk sets can only be played on the website (the browser emulator unpacks them);
                    // a real C64 cannot mount a ZIP, so say so instead of failing later on LOAD
                    if (fileName.ToLower().EndsWith(".zip"))
                    {
                        WriteResponse(httpListenerResponse, $"\r\nERROR: {fileName} IS A ZIP DISK SET.\r\nPLAY IT ON 8BITFLYNN.IO\0", session);
                        return;
                    }

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

                // IMAGE (website): the whole disk image for a search result, so the browser emulator's own
                // 1541 can run it (fast loaders and all). Same request body as /mount: [sessLo sessHi len id...]
                if (httpListenerRequest.HttpMethod == "POST" && basePath.Equals("/image", StringComparison.OrdinalIgnoreCase))
                {
                    HandleImageRequest(httpListenerRequest, httpListenerResponse, sessionProvider);
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

        // POST /image  [sessLo sessHi len id...]  (id = number from the last search, never a name or path)
        //   ok:    application/octet-stream  [sessLo sessHi nameLen name...] + raw .d64/.g64/.zip bytes
        //          (a Commodore.Software download with several disks comes back as one .zip)
        //   error: text/plain  [sessLo sessHi] + "ERROR: ...\0"  (same as /mount)
        private void HandleImageRequest(HttpListenerRequest request, HttpListenerResponse response, ISessionProvider sessionProvider)
        {
            HttpMountRequest imageRequest;
            try
            {
                imageRequest = HttpMountRequest.ParseFromBytes(ParseMultipartDataBytes(request));
            }
            catch (ArgumentException ex)
            {
                this.Logger.LogMessage($"[IMAGE] Invalid request: {ex.Message}");
                WriteResponse(response, "ERROR: Invalid image request\0", null);
                return;
            }

            Session session = sessionProvider.GetOrCreateSession(imageRequest.SessionId);
            string idText = imageRequest.GetImageIdOrFilenameString().TrimEnd();

            if (idText.Length > 5 || !int.TryParse(idText, out int id) || id < 1 || id > this.Configuration.MaxSearchResults)
            {
                WriteResponse(response, "\r\nERROR: INVALID FLOPPY ID\0", session);
                return;
            }

            FloppyIdentifier floppyIdentifier = new FloppyIdentifier { IdLo = (byte)(id & 0xFF), IdHi = (byte)(id >> 8) };
            FloppyInfo floppyInfo = session.FloppyResolver.InsertFloppy(floppyIdentifier);
            string imagePath = session.FloppyResolver.GetInsertedFloppyPointer().ImagePath;

            if (floppyInfo.Equals(default(FloppyInfo)) || string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
            {
                WriteResponse(response, "\r\nERROR: FLOPPY NOT FOUND\0", session);
                return;
            }

            string extension = Path.GetExtension(imagePath).ToLowerInvariant();
            if (!WholeImageExtensions.Contains(extension))
            {
                WriteResponse(response, "\r\nERROR: NOT A DISK IMAGE\0", session);
                return;
            }

            try
            {
                string imageName = Path.GetFileName(imagePath);
                byte[] imageBytes;

                IReadOnlyList<string> diskSet = (session.FloppyResolver as CommodoreSoftwareFloppyResolver)?.InsertedDiskSet;
                string setName = null;
                if (diskSet == null && extension != ".zip" && session.FloppyResolver is LocalFloppyResolver
                    && this.Configuration.FloppyResolverSettings?.Local?.GroupMultiDiskImages == true)
                {
                    diskSet = FindMultiDiskSet(imagePath, GetDiskMarker(), out setName);
                    if (diskSet != null)
                    {
                        this.Logger.LogMessage($"[IMAGE] Multi-disk set: {string.Join(", ", diskSet.Select(Path.GetFileName))}");
                    }
                }

                if (extension != ".zip" && diskSet != null && diskSet.Count > 1)
                {
                    if (diskSet.Count > MAX_DISKS_FOR_BROWSER)
                    {
                        this.Logger.LogMessage($"[IMAGE] {diskSet.Count} disks in this download, sending the first {MAX_DISKS_FOR_BROWSER}");
                    }
                    imageBytes = ZipDiskSet(diskSet.Take(MAX_DISKS_FOR_BROWSER));
                    imageName = (setName ?? Path.GetFileNameWithoutExtension(imagePath)) + ".zip";
                }
                else
                {
                    if (new FileInfo(imagePath).Length > MAX_IMAGE_SIZE)
                    {
                        WriteResponse(response, "\r\nERROR: IMAGE TOO LARGE\0", session);
                        return;
                    }
                    imageBytes = File.ReadAllBytes(imagePath);
                }

                if (imageBytes.Length > MAX_IMAGE_SIZE)
                {
                    WriteResponse(response, "\r\nERROR: IMAGE TOO LARGE\0", session);
                    return;
                }

                this.Logger.LogMessage($"[IMAGE] Sending {imageName} ({imageBytes.Length} bytes)");
                WriteImageResponse(response, session, imageName, imageBytes);
            }
            catch (Exception ex)
            {
                this.Logger.LogMessage($"[IMAGE] Error: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Error);
                WriteResponse(response, "\r\nERROR: COULD NOT READ IMAGE\0", session);
            }
        }

        // Multi-disk games in a local folder: "California Games - Disk1.d64", "California Games - Disk2.d64",
        // also "Disk 1", "(Disk 1 of 2)", "Side A", "Side 2". Only a set when another disk with the same name sits
        // in the same folder, so single-disk "Name - Disk1.d64" files and names like "Bruce Lee II" are left alone.
        // The marker words come from appsettings (Local.DiskMarkers), so no rebuild is needed to add one.
        private static readonly string[] DefaultDiskMarkers = { "disk", "disc", "side" };
        private Regex diskMarker;

        private Regex GetDiskMarker()
        {
            if (this.diskMarker == null)
            {
                IEnumerable<string> words = this.Configuration.FloppyResolverSettings?.Local?.DiskMarkers?
                    .Where(word => !string.IsNullOrWhiteSpace(word)).Select(word => word.Trim()).ToList();
                if (words == null || !words.Any())
                {
                    words = DefaultDiskMarkers;
                }
                string alternatives = string.Join("|", words.OrderByDescending(word => word.Length).Select(Regex.Escape));
                this.diskMarker = new Regex(
                    @"^(?<base>.*?)[\s_\-]*[\(\[]?\s*(?:" + alternatives + @")\s*(?:(?<n>\d{1,2})(?<side>[a-h])?|(?<side>[a-h]))(?:\s*of\s*\d{1,2})?\s*[\)\]]?(?:[\s_\-]+label\s*(?:up|down))?$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }
            return this.diskMarker;
        }

        private static IReadOnlyList<string> FindMultiDiskSet(string imagePath, Regex diskMarker, out string setName)
        {
            setName = null;
            string folder = Path.GetDirectoryName(imagePath);
            Match marker = diskMarker.Match(Path.GetFileNameWithoutExtension(imagePath));
            if (string.IsNullOrEmpty(folder) || !marker.Success)
            {
                return null;
            }

            string baseName = marker.Groups["base"].Value.Trim();
            if (baseName.Length == 0)
            {
                return null;
            }

            List<string> disks = Directory.EnumerateFiles(folder)
                .Where(file => { string ext = Path.GetExtension(file).ToLowerInvariant(); return ext == ".d64" || ext == ".g64"; })
                .Select(file => new { File = file, Match = diskMarker.Match(Path.GetFileNameWithoutExtension(file)) })
                .Where(x => x.Match.Success && string.Equals(x.Match.Groups["base"].Value.Trim(), baseName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => DiskOrder(x.Match))
                .ThenBy(x => x.File, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.File)
                .ToList();

            if (disks.Count < 2)
            {
                return null;
            }
            setName = baseName;
            return disks;
        }

        // Disk1 < Disk1A < Disk1B < Disk2; a side on its own ("Side A") counts like a disk number (A = 1)
        private static int DiskOrder(Match match)
        {
            int side = match.Groups["side"].Success ? char.ToUpperInvariant(match.Groups["side"].Value[0]) - 'A' + 1 : 0;
            if (!match.Groups["n"].Success)
            {
                return side * 100;
            }
            return int.Parse(match.Groups["n"].Value) * 100 + side;
        }

        private static byte[] ZipDiskSet(IEnumerable<string> diskPaths)
        {
            using (var buffer = new MemoryStream())
            {
                using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (string diskPath in diskPaths)
                    {
                        archive.CreateEntryFromFile(diskPath, Path.GetFileName(diskPath), CompressionLevel.Fastest);
                    }
                }
                return buffer.ToArray();
            }
        }

        private void WriteImageResponse(HttpListenerResponse response, Session session, string imageName, byte[] imageBytes)
        {
            try
            {
                // name as plain ASCII, max 64 chars, so the website can show it and pick the right loader (.d64 / .zip)
                byte[] nameBytes = Encoding.ASCII.GetBytes(new string(imageName.Select(c => c >= 32 && c < 127 ? c : '_').Take(64).ToArray()));

                byte[] fullResponse = new byte[3 + nameBytes.Length + imageBytes.Length];
                fullResponse[0] = (byte)(session.SessionId & 0xFF);
                fullResponse[1] = (byte)(session.SessionId >> 8);
                fullResponse[2] = (byte)nameBytes.Length;
                Buffer.BlockCopy(nameBytes, 0, fullResponse, 3, nameBytes.Length);
                Buffer.BlockCopy(imageBytes, 0, fullResponse, 3 + nameBytes.Length, imageBytes.Length);

                response.SendChunked = false;
                response.StatusCode = 200;
                response.ContentType = "application/octet-stream";
                response.ContentLength64 = fullResponse.Length;

                var writeTask = response.OutputStream.WriteAsync(fullResponse, 0, fullResponse.Length);
                int timeoutMs = 5000 + (fullResponse.Length / 1024 * 100);
                if (!writeTask.Wait(timeoutMs))
                {
                    this.Logger.LogMessage($"[IMAGE-WRITE] Timeout after {timeoutMs}ms", VDRIVE_Contracts.Enums.LogSeverity.Error);
                    throw new TimeoutException("Write timeout");
                }

                response.OutputStream.Flush();
                response.Close();
            }
            catch (Exception ex)
            {
                this.Logger.LogMessage($"[IMAGE-WRITE] Error: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Error);
                try { response?.Close(); } catch { }
            }
        }

        private void ExtractUrlParts(HttpListenerRequest httpListenerRequest, out string token, out string basePath)
        {
            // Allow optional token segment in the URL: e.g. /search/token or just /search if token are not used
            token = "";
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
                        token = parts[1];
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

        private bool IsValidLoadAddress(ILogger logger, ushort dest_ptr_start, int end_dest_ptr)
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

            if (end_dest_ptr >= 0xc000) // VDRIVE memory area (make this check configurable?)
            {
                logger.LogMessage($"Load end 0x{end_dest_ptr:X4} overlaps VDRIVE memory, rejecting", VDRIVE_Contracts.Enums.LogSeverity.Warning);
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

        // "@HVSC" -> "HvscPsid" (the names FloppyResolverFactory understands); null if unknown
        private static string GetResolverTypeForCommand(string command)
        {
            switch (command.TrimStart('@').ToUpperInvariant())
            {
                case "LOCAL": return "Local";
                case "CS":
                case "COMMODORESOFTWARE": return "CommodoreSoftware";
                case "HVSC": return "HvscPsid";
                case "C64": return "C64";
                default: return null;
            }
        }

        // name shown in the results header for the session's current source
        private string GetSourceName(Session session)
        {
            switch (session?.FloppyResolver)
            {
                case LocalFloppyResolver _: return "LOCAL";
                case CommodoreSoftwareFloppyResolver _: return "COMMODORE.SOFTWARE";
                case HvscPsidFloppyResolver _: return "HVSC";
                case C64FloppyResolver _: return "C64";
                default: return (this.Configuration.FloppyResolver ?? "").ToUpper();
            }
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
                ? $"\r\n\r\n{this.Configuration.SearchIntroMessage.ToUpper()}\r\n\r\n{GetSourceName(session)} RESULTS: \"{session.LastSearchTerm}\"\r\n\r\n"
                : $"\r\n\r\n{GetSourceName(session)} RESULTS: \"{session.LastSearchTerm}\"\r\n\r\n";

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
