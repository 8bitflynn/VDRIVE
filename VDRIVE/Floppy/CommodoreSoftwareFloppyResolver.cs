using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Floppy
{
    public class CommodoreSoftwareFloppyResolver : FloppyResolverBase, IFloppyResolver
    {
        public CommodoreSoftwareFloppyResolver(IConfiguration configuration, ILog logger)
        {
            this.Configuration = configuration;
            this.Logger = logger;

            this.MediaExtensionsAllowed = this.DefaultMediaExtensionsAllowed;
        }

        // TODO: move to config...
        List<string> IgnoredSearchKeywords = new List<string> { "manual", "firmware", "documentation", "guide", "instruction", "tutorial", 
         "c128", "dos", "128" };

        List<string> DefaultMediaExtensionsAllowed = new List<string> { ".d64", ".g64", ".d81", ".d71", ".d80", ".d82", ".tap", ".t64", ".prg", ".p00" };
        
        // set when searching disk
        List<string> MediaExtensionsAllowed = new List<string>();
      
        public override FloppyInfo? InsertFloppy(FloppyIdentifier floppyIdentifier)
        {
            FloppyInfo? floppyInfo = base.InsertFloppy(floppyIdentifier);

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string downloadPageUrl = this.BuildFullCommodoreSoftwarePath(this.InsertedFloppyPointer.Value.ImagePath);
                    HttpResponseMessage httpResponseMessage = client.PostAsync(downloadPageUrl, null).Result;
                    string html = httpResponseMessage.Content.ReadAsStringAsync().Result;

                    if (httpResponseMessage.IsSuccessStatusCode)
                    {
                        var match = Regex.Match(html, @"([^""]+)""\s+aria-label=""Start download process""");

                        if (match.Success)
                        {
                            string rawHref = match.Groups[1].Value;
                            string decodedHref = WebUtility.HtmlDecode(rawHref);

                            this.Logger.LogMessage("Extracted link: " + decodedHref);

                            byte[] zippedFile = this.DownloadFile(decodedHref);
                            if (zippedFile == null)
                            {
                                this.Logger.LogMessage("Failed to download file.");
                                return null;
                            }
                            this.Decompress(zippedFile);
                        }
                    }
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
                    string downloadUrl = this.BuildFullCommodoreSoftwarePath(decodedUrl);

                    HttpResponseMessage httpResponseMessage = httpClient.GetAsync(downloadUrl).GetAwaiter().GetResult();
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

        public override SearchFloppyResponse SearchFloppys(SearchFloppiesRequest searchFloppiesRequest)
        {
            // clear previous search results
            this.EjectFloppy();

            if (string.IsNullOrWhiteSpace(searchFloppiesRequest.MediaType))
            {
                this.MediaExtensionsAllowed = this.DefaultMediaExtensionsAllowed;
            }
            else
            {
                this.MediaExtensionsAllowed = searchFloppiesRequest.MediaType.Split(',').ToList();
            }

            using (HttpClient client = new HttpClient())
            {
                string searchTerm = new string(searchFloppiesRequest.SearchTerm);
                string mediaType = new string(searchFloppiesRequest.MediaType);

                string searchUrl = this.BuildCommodoreSoftwareSearchUrl(searchTerm, mediaType);

                this.Logger.LogMessage($"Searching Commodore Software for '{searchTerm}' with media type '{mediaType}'");

                HttpResponseMessage httpResponseMessage = client.PostAsync(searchUrl, null).Result;
                string html = httpResponseMessage.Content.ReadAsStringAsync().Result;

                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    SearchFloppyResponse searchFloppyResponse = new SearchFloppyResponse();
                    searchFloppyResponse.ResponseCode = 0x00; // success

                    IEnumerable<FloppyInfo> floppyInfos = this.ScrapeResults(html);
                    searchFloppyResponse.SearchResults = floppyInfos.ToArray();

                    this.Logger.LogMessage($"Found {floppyInfos.Count()} results for '{searchTerm}'");

                    return searchFloppyResponse;
                }
                else
                {
                    this.Logger.LogMessage(@"Failed to search {searchTerm}: " + httpResponseMessage.StatusCode);
                }
            }

            return default(SearchFloppyResponse);
        }       

        private IEnumerable<FloppyInfo> ScrapeResults(string html)
        {
            List<FloppyInfo> floppyInfos = new List<FloppyInfo>();

            // Match each result block
            string pattern = @"<dt class=""result-title"">.*?<a href=""(.*?)"".*?>(.*?)</a>.*?</dt>.*?<dd class=""result-text"">.*?>(.*?)</dd>";
            var matches = Regex.Matches(html, pattern, RegexOptions.Singleline);

            ushort searchResultIndex = 1;
            foreach (Match match in matches)
            {
                string imageName = match.Groups[2].Value.Trim();
                if (IgnoredSearchKeywords.Any(ir => imageName.ToLower().Contains(ir.ToLower())))
                {
                    continue;
                }

                FloppyInfo floppyInfo = new FloppyInfo();
                floppyInfo.IdLo = (byte)searchResultIndex;
                floppyInfo.IdHi = (byte)(searchResultIndex >> 8);
                floppyInfo.ImageName = match.Groups[2].Value.Trim().ToCharArray();
                floppyInfo.ImageNameLength = (byte)floppyInfo.ImageName.TakeWhile(c => c != '\0').Count();
                // strip html
                string description = Regex.Replace(match.Groups[3].Value, @"<.*?>", "").Trim();
                // strip non-ascii
                description = Regex.Replace(description, @"[^\x20-\x7E]", "");
                // strip multiple spaces
                description = Regex.Replace(new string(floppyInfo.Description), @"\s{2,}", " ");
                floppyInfo.Description = description.ToCharArray();
                floppyInfo.DescriptionLength = (byte)floppyInfo.Description.TakeWhile(c => c != '\0').Count();  
                floppyInfos.Add(floppyInfo);

                // floppy pointers hold Id and long path and are looked up when "inserted"
                FloppyPointer floppyPointer = new FloppyPointer();
                floppyPointer.Id = searchResultIndex;
                floppyPointer.ImagePath = match.Groups[1].Value.Trim();

                // results stored in resolver for lookup if "inserted"
                this.FloppyInfos.Add(floppyInfo);
                this.FloppyPointers.Add(floppyPointer);

                searchResultIndex++;
            }

            return floppyInfos;
        }

        private string BuildCommodoreSoftwareSearchUrl(string searchTerm, string mediaType)
        {
            string baseUrl = this.BuildFullCommodoreSoftwarePath("/search/search?");
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

        private string BuildFullCommodoreSoftwarePath(string relativePath)
        {
            return "https://commodore.software" + relativePath;
        }
    }
}
