using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Floppy
{
    public class C64FloppyResolverFloppyResolver : FloppyResolverBase, IFloppyResolver
    {
        public C64FloppyResolverFloppyResolver(IConfiguration configuration, ILog logger)
        {
            this.Configuration = configuration;
            this.Logger = logger;
            this.MediaExtensionsAllowed = this.DefaultMediaExtensionsAllowed;
        }

        public override FloppyInfo? InsertFloppy(FloppyIdentifier floppyIdentifier)
        {
            FloppyInfo? floppyInfo = base.InsertFloppy(floppyIdentifier);

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string downloadUrl = "https://www.c64.com/games/" + this.InsertedFloppyPointer.Value.ImagePath;

                    byte[] zippedFile = this.DownloadFile(downloadUrl);
                    if (zippedFile == null)
                    {
                        this.Logger.LogMessage("Failed to download file.");
                        return null;
                    }
                    this.Decompress(zippedFile);                
                }
            }
            catch (Exception exception)
            {
                this.Logger.LogMessage("Failed to insert floppy: " + exception.Message);
                return null;
            }

            return floppyInfo;
        }

        private byte[] DownloadFile(string url)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                try
                {
                    string decodedUrl = WebUtility.HtmlDecode(url);

                    HttpResponseMessage httpResponseMessage = httpClient.GetAsync(decodedUrl).GetAwaiter().GetResult();
                    httpResponseMessage.EnsureSuccessStatusCode();

                    byte[] fileBytes = httpResponseMessage.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    this.Logger.LogMessage($"Download complete ({(fileBytes != null ? fileBytes.Length : 0)} bytes): " + new string(this.InsertedFloppyInfo.Value.ImageName));

                    return fileBytes;
                }
                catch (Exception ex)
                {
                    this.Logger.LogMessage("Download failed: " + ex.Message);
                    return null;
                }
            }
        }

        private void Decompress(byte[] zipBytes)
        {
            using (var zipStream = new MemoryStream(zipBytes))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    // directory (may not be C64 software)
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }

                    this.Logger.LogMessage($"Extracting: {entry.FullName} {entry.Length} bytes");

                    // Skip directories
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    // Build full output path
                    string fullFilePath = Path.Combine(this.Configuration.TempPath, entry.FullName);

                    // Ensure directory exists
                    if (!Directory.Exists(Path.GetDirectoryName(fullFilePath)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(fullFilePath));
                    }

                    // Extract file
                    entry.ExtractToFile(fullFilePath, overwrite: true);

                    if (this.InsertedFloppyPointer.HasValue)
                    {

                        if (!this.MediaExtensionsAllowed.Any(ir => fullFilePath.ToLower().EndsWith(ir)))
                        {
                            continue;
                        }

                        FloppyInfo tempFloppyInfo = this.InsertedFloppyInfo.Value;
                        tempFloppyInfo.ImageName = Path.GetFileName(fullFilePath).ToCharArray();
                        this.InsertedFloppyInfo = tempFloppyInfo; // update to extracted file name

                        FloppyPointer tempFloppyPointer = this.InsertedFloppyPointer.Value;
                        tempFloppyPointer.ImagePath = fullFilePath;
                        this.InsertedFloppyPointer = tempFloppyPointer; // update to extracted file path                        
                    }
                }
            }

            return;
        }

        public override SearchFloppyResponse SearchFloppys(SearchFloppiesRequest searchFloppiesRequest, out FloppyInfo[] foundFloppyInfos)
        {
            // clear previous search results
            this.FloppyInfos.Clear();
            this.FloppyPointers.Clear();

            string mediaType = searchFloppiesRequest.MediaType != null ? new string(searchFloppiesRequest.MediaType.TakeWhile(c => c != '\0').ToArray()) : string.Empty;
            string searchTerm = new string(searchFloppiesRequest.SearchTerm.TakeWhile(c => c != '\0').ToArray());

            if (string.IsNullOrWhiteSpace(mediaType))
            {
                this.MediaExtensionsAllowed = this.DefaultMediaExtensionsAllowed;
            }
            else
            {
                this.MediaExtensionsAllowed = mediaType.Split(',').ToList();
            }

            using (HttpClient client = new HttpClient())
            {
                string searchUrl = this.BuildC64SearchUrl(searchTerm, mediaType);

                this.Logger.LogMessage($"Searching C64.com for '{searchTerm}' with media type '{mediaType}'");

                var fields = new Dictionary<string, string>
                {
                    ["searchtype"] = "0",
                    ["searchfor"] = searchTerm,
                    ["main"] = "1",
                };

                using var content = new FormUrlEncodedContent(fields);
                HttpResponseMessage httpResponseMessage = client.PostAsync(searchUrl, content).Result;
                string html = httpResponseMessage.Content.ReadAsStringAsync().Result;

                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    IEnumerable<FloppyInfo> floppyInfos = this.ScrapeResults(html);

                    SearchFloppyResponse searchFloppyResponse = this.BuildSearchFloppyResponse(4096, (floppyInfos.Count() > 0 ? (byte)0xff : (byte)0x04), (byte)floppyInfos.Count()); // more follows


                    //foundFloppyInfos = floppyInfos.ToArray();
                    foundFloppyInfos = floppyInfos.Take(this.Configuration.MaxSearchResults).ToArray();


                    this.Logger.LogMessage($"Found {floppyInfos.Count()} results for '{searchTerm}'");

                    return searchFloppyResponse;
                }
                else
                {
                    this.Logger.LogMessage(@"Failed to search {searchTerm}: " + httpResponseMessage.StatusCode);
                }
            }
            foundFloppyInfos = null;
            return default(SearchFloppyResponse);
        }       

        protected SearchFloppyResponse BuildSearchFloppyResponse(ushort destPtr, byte responseCode, byte resultCount, ushort chunkSize = 1024)
        {
            SearchFloppyResponse searchFloppyResponse = new SearchFloppyResponse();
            searchFloppyResponse.ResponseCode = responseCode;

            if (resultCount > 0)
            {
                searchFloppyResponse.SyncByte = (byte)'+';
                searchFloppyResponse.ResultCount = resultCount;

                // filled in later
                // send binary length in 24 bits
                //int lengthMinusMemoryPtr = payload.Length - 2;
                //searchFloppyResponse.ByteCountLo = (byte)(lengthMinusMemoryPtr & 0xFF); // LSB
                //searchFloppyResponse.ByteCountMid = (byte)((lengthMinusMemoryPtr >> 8) & 0xFF);
                //searchFloppyResponse.ByteCountHi = (byte)((lengthMinusMemoryPtr >> 16) & 0xFF); // MSB

                byte loChunkLength = (byte)chunkSize;
                byte hiChunkLength = (byte)(chunkSize >> 8);
                searchFloppyResponse.ChunkSizeLo = loChunkLength;
                searchFloppyResponse.ChunkSizeHi = hiChunkLength;

                // int memoryLocation = (payload[1] << 8) + payload[0];

                byte loDestPtr = (byte)destPtr;
                byte hiDestPtr = (byte)(destPtr >> 8);

                searchFloppyResponse.DestPtrLo = loDestPtr;
                searchFloppyResponse.DestPtrHi = hiDestPtr;
            }

            return searchFloppyResponse;
        }

        private IEnumerable<FloppyInfo> ScrapeResults(string html)
        {
            List<FloppyInfo> floppyInfos = new List<FloppyInfo>();

            var nameRegex = new Regex(@"<b>(?<name>[^<]+)</b>", RegexOptions.IgnoreCase);
            var linkRegex = new Regex(@"href\s*=\s*[""'](?<url>[^""']*download\.php\?[^""']*)[""']", RegexOptions.IgnoreCase);

            var nameMatches = nameRegex.Matches(html);
            var linkMatches = linkRegex.Matches(html);

           // var results = new List<(string name, string url)>();

            ushort searchResultIndex = 1;
            foreach (Match nameMatch in nameMatches)
            {
                int nameIndex = nameMatch.Index;
                string imageName = nameMatch.Groups["name"].Value;

                // Find the first link that appears after this name
                Match linkMatch = linkMatches
                    .Cast<Match>()
                    .FirstOrDefault(l => l.Index > nameIndex);

                if (linkMatch != null)
                {
                    string url = linkMatch.Groups["url"].Value;
                    //  results.Add((name, url));

                    FloppyInfo floppyInfo = new FloppyInfo();
                    floppyInfo.IdLo = (byte)searchResultIndex;
                    floppyInfo.IdHi = (byte)(searchResultIndex >> 8);

                    if (imageName.Length > 64)
                    {
                        imageName = imageName.Substring(0, 64);
                    }
                    floppyInfo.ImageNameLength = (byte)imageName.Length;
                    floppyInfo.ImageName = new char[64];
                    imageName.ToUpper().ToCharArray().CopyTo(floppyInfo.ImageName, 0);
                    floppyInfos.Add(floppyInfo);

                    // floppy pointers hold Id and long path and are looked up when "inserted"
                    FloppyPointer floppyPointer = new FloppyPointer();
                    floppyPointer.Id = searchResultIndex;
                    floppyPointer.ImagePath = url;

                    // results stored in resolver for lookup if "inserted"
                    this.FloppyInfos.Add(floppyInfo);
                    this.FloppyPointers.Add(floppyPointer);

                    searchResultIndex++;
                }
            }
        

            return floppyInfos;
        }

        private string BuildC64SearchUrl(string searchTerm, string mediaType)
        {
            string baseUrl = this.BuildFullC64Path("/search/search?");
            var queryParams = new Dictionary<string, string>
            {
                { "searchword", searchTerm },
                { "Search", "" },
                { "task", "search" },
                { "reset", "" },
                { "searchphrase", "all" },
                { "ordering", "popular" }, // newest
                { "limit", "0" } // all
            };

            var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
            return baseUrl + queryString;
        }

        private string BuildFullC64Path(string relativePath)
        {
            return "https://www.c64.com/games/no-frame.php" + relativePath;
        }
    }
}
