using System.Net;
using System.Text.RegularExpressions;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Floppy
{
    public class CommodoreSoftwareFloppyResolver : RemoteFloppyResolverBase, IFloppyResolver
    {
        public CommodoreSoftwareFloppyResolver(IConfiguration configuration, IVDriveLoggger logger)
        {
            this.Configuration = configuration;
            this.Logger = logger;
        }

        List<string> IgnoredSearchKeywords = new List<string> { "manual", "firmware", "documentation", "guide", "instruction", "tutorial", 
         "c128", "dos", "128" };        
      
        public override FloppyInfo InsertFloppy(FloppyIdentifier floppyIdentifier)
        {
            FloppyInfo floppyInfo = base.InsertFloppy(floppyIdentifier);

            if (floppyInfo.Equals(default(FloppyInfo)))
            {
                return floppyInfo;
            }

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string downloadPageUrl = this.BuildFullCommodoreSoftwarePath(this.InsertedFloppyPointer.ImagePath);
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

                            string commodoreSoftwareDownloadUrl = this.BuildFullCommodoreSoftwarePath(decodedHref);

                            byte[] zippedFile = this.DownloadFile(commodoreSoftwareDownloadUrl);
                            if (zippedFile == null)
                            {
                                this.Logger.LogMessage("Failed to download file.");
                                return default(FloppyInfo);
                            }
                            this.Decompress(zippedFile);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                this.Logger.LogMessage("Failed to insert floppy: " + exception.Message);
                return default(FloppyInfo);
            }
            
            return floppyInfo;
        }       

        public override SearchFloppyResponse SearchFloppys(SearchFloppiesRequest searchFloppiesRequest, out FloppyInfo[] foundFloppyInfos)
        {
            // clear previous search results
            this.FloppyInfos.Clear();
            this.FloppyPointers.Clear();
          
            string searchTerm = new string(searchFloppiesRequest.SearchTerm.TakeWhile(c => c != '\0').ToArray());
            string mediaType = searchFloppiesRequest.MediaType != null ? new string(searchFloppiesRequest.MediaType.TakeWhile(c => c != '\0').ToArray()) : string.Empty;
            
            using (HttpClient client = new HttpClient())
            {
                string searchUrl = this.BuildCommodoreSoftwareSearchUrl(searchTerm, mediaType);

                this.Logger.LogMessage($"Searching Commodore Software for '{searchTerm}' with media type '{mediaType}'");

                HttpResponseMessage httpResponseMessage = client.PostAsync(searchUrl, null).Result;
                string html = httpResponseMessage.Content.ReadAsStringAsync().Result;

                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    IEnumerable<FloppyInfo> floppyInfos = this.ScrapeResults(html);

                    SearchFloppyResponse searchFloppyResponse = this.BuildSearchFloppyResponse(4096, (floppyInfos.Count() > 0 ? (byte)0xff: (byte)0x04), (byte)floppyInfos.Count()); // more follows

                    // foundFloppyInfos = floppyInfos.ToArray();
                    foundFloppyInfos = floppyInfos.Take(this.Configuration.MaxSearchResults).ToArray();
                    searchFloppyResponse.ResultCount = (byte)foundFloppyInfos.Length;

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

        private IEnumerable<FloppyInfo> ScrapeResults(string html)
        {
            List<FloppyInfo> floppyInfos = new List<FloppyInfo>();

            // Match each result block
            string pattern = @"<dt class=""result-title"">.*?<a href=""(.*?)"".*?>(.*?)</a>.*?</dt>.*?<dd class=""result-text"">.*?>(.*?)</dd>";
            var matches = Regex.Matches(html, pattern, RegexOptions.Singleline);

            ushort searchResultIndexId = 1;
            foreach (Match match in matches)
            {
                string imageName = match.Groups[2].Value.Trim();
                if (IgnoredSearchKeywords.Any(ir => imageName.ToLower().Contains(ir.ToLower())))
                {
                    continue;
                }

                FloppyInfo floppyInfo = new FloppyInfo();
                floppyInfo.IdLo = (byte)searchResultIndexId;
                floppyInfo.IdHi = (byte)(searchResultIndexId >> 8);

                if (imageName.Length > 64)
                {
                    imageName = imageName.Substring(0, 64);
                }
                floppyInfo.ImageNameLength = (byte)imageName.Length;
                floppyInfo.ImageName = new char[64];
                imageName.ToUpper().ToCharArray().CopyTo(floppyInfo.ImageName, 0);

                // strip html
                string description = Regex.Replace(match.Groups[3].Value, @"<.*?>", "").Trim();
                // strip non-ascii
                description = Regex.Replace(description, @"[^\x20-\x7E]", "");               
                floppyInfos.Add(floppyInfo);

                // floppy pointers hold Id and long path and are looked up when "inserted"
                FloppyPointer floppyPointer = new FloppyPointer();
                floppyPointer.Id = searchResultIndexId;
                floppyPointer.ImagePath = match.Groups[1].Value.Trim();

                // results stored in resolver for lookup if "inserted"
                this.FloppyInfos.Add(floppyInfo);
                this.FloppyPointers.Add(floppyPointer);

                searchResultIndexId++;
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
