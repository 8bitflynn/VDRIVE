using System.Net;
using System.Text.RegularExpressions;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Floppy.Impl
{
    public class CommodoreSoftwareFloppyResolver : RemoteFloppyResolverBase, IFloppyResolver
    {
        public CommodoreSoftwareFloppyResolver(IConfiguration configuration, ILogger logger)
        {
            Configuration = configuration;
            Logger = logger;
        }

        public override FloppyInfo InsertFloppy(FloppyIdentifier floppyIdentifier)
        {
            FloppyInfo floppyInfo = base.InsertFloppy(floppyIdentifier);

            if (floppyInfo.Equals(default))
            {
                return floppyInfo;
            }

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string downloadPageUrl = BuildFullCommodoreSoftwarePath(InsertedFloppyPointer.ImagePath);
                    HttpResponseMessage httpResponseMessage = client.PostAsync(downloadPageUrl, null).Result;
                    string html = httpResponseMessage.Content.ReadAsStringAsync().Result;

                    if (httpResponseMessage.IsSuccessStatusCode)
                    {
                        var match = Regex.Match(html, @"([^""]+)""\s+aria-label=""Start download process""");

                        if (match.Success)
                        {
                            string rawHref = match.Groups[1].Value;
                            string decodedHref = WebUtility.HtmlDecode(rawHref);

                            Logger.LogMessage("Extracted link: " + decodedHref);

                            string commodoreSoftwareDownloadUrl = BuildFullCommodoreSoftwarePath(decodedHref);

                            byte[] zippedFile = DownloadFile(commodoreSoftwareDownloadUrl);
                            if (zippedFile == null)
                            {
                                Logger.LogMessage("Failed to download file.");
                                return default;
                            }

                            // attempt to get disk1
                            IEnumerable<string> extractedFilePaths = this.DecompressArchive(zippedFile);
                            string fullFilePath = this.ResolvePrimaryDisk(extractedFilePaths, Configuration.FloppyResolverSettings.CommodoreSoftware.MediaExtensionsAllowed);

                            this.InsertedFloppyInfo.ImageName = Path.GetFileName(fullFilePath).ToCharArray();                          
                            this.InsertedFloppyPointer.ImagePath = fullFilePath; // update to disk image extracted file path                        
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                Logger.LogMessage("Failed to insert floppy: " + exception.Message);
                return default;
            }

            return floppyInfo;
        }       

        public override SearchFloppyResponse SearchFloppys(SearchFloppiesRequest searchFloppiesRequest, out FloppyInfo[] foundFloppyInfos)
        {
            // clear previous search results
            ClearSearchResults();

            string searchTerm;
            if (searchFloppiesRequest.SearchTermLength != 0)
            {
                searchTerm = new string(searchFloppiesRequest.SearchTerm.TakeWhile(c => c != '\0').ToArray());
            }
            else
            {
                searchTerm = string.Empty;
            }

            string mediaTypeCSV;
            if (searchFloppiesRequest.MediaTypeLength != 0)
            {
                mediaTypeCSV = new string(searchFloppiesRequest.MediaType.TakeWhile(c => c != '\0').ToArray()).TrimEnd();
            }
            else
            {
                mediaTypeCSV = string.Join(',', Configuration.FloppyResolverSettings.Local.MediaExtensionsAllowed);
            }

            Logger.LogMessage($"Searching Commodore.Software.com for description '{searchTerm}' and media type '{mediaTypeCSV}'");

            using (HttpClient client = new HttpClient())
            {
                string searchUrl = BuildCommodoreSoftwareSearchUrl(searchTerm, mediaTypeCSV);

                HttpResponseMessage httpResponseMessage = client.PostAsync(searchUrl, null).Result;
                string html = httpResponseMessage.Content.ReadAsStringAsync().Result;

                if (httpResponseMessage.IsSuccessStatusCode)
                {
                    IEnumerable<FloppyInfo> floppyInfos = ScrapeResults(html);

                    SearchFloppyResponse searchFloppyResponse = BuildSearchFloppyResponse(0x1000, floppyInfos.Count() > 0 ? (byte)0xff : (byte)0x04, (byte)floppyInfos.Count()); // more follows
                    foundFloppyInfos = floppyInfos.Take(Configuration.MaxSearchResults).ToArray();
                    searchFloppyResponse.ResultCount = (byte)foundFloppyInfos.Length;

                    return searchFloppyResponse;
                }
                else
                {
                    Logger.LogMessage($"Failed to search {searchTerm}: " + httpResponseMessage.StatusCode, VDRIVE_Contracts.Enums.LogSeverity.Error);
                }
            }

            foundFloppyInfos = null;
            return default;
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
                if (Configuration.FloppyResolverSettings.CommodoreSoftware.IgnoredSearchKeywords.Any(ir => imageName.ToLower().Contains(ir.ToLower())))
                {
                    // skip this result as it is ignored
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
                FloppyInfos.Add(floppyInfo);
                FloppyPointers.Add(floppyPointer);

                searchResultIndexId++;
            }

            return floppyInfos;
        }

        private string BuildCommodoreSoftwareSearchUrl(string searchTerm, string mediaType) // media type not currently used
        {
            string baseUrl = BuildFullCommodoreSoftwarePath("/search/search?");
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
            return this.Configuration.FloppyResolverSettings.CommodoreSoftware.BaseURL + relativePath;
        }
    }
}
