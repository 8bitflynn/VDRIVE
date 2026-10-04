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

            // Commodore.Software (Joomla) now wants the search as a POST form that includes the
            // per-session security token (a hidden field: 32 hex chars as the name, "1" as the value).
            // So: load the search page with a cookie jar, read the token, then post the form with the same cookies.
            using (HttpClientHandler handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true })
            using (HttpClient client = new HttpClient(handler))
            {
                string searchUrl = BuildFullCommodoreSoftwarePath("/search/search");

                string searchPage = client.GetStringAsync(searchUrl).Result;
                Match tokenMatch = Regex.Match(searchPage, @"name=""([0-9a-f]{32})""\s+value=""1""", RegexOptions.IgnoreCase);
                if (!tokenMatch.Success)
                {
                    tokenMatch = Regex.Match(searchPage, @"value=""1""\s+name=""([0-9a-f]{32})""", RegexOptions.IgnoreCase); // attribute order varies
                }
                if (!tokenMatch.Success)
                {
                    Logger.LogMessage("Commodore.Software search token not found on the search page", VDRIVE_Contracts.Enums.LogSeverity.Error);
                }

                FormUrlEncodedContent form = BuildCommodoreSoftwareSearchForm(searchTerm, tokenMatch.Success ? tokenMatch.Groups[1].Value : null);

                HttpResponseMessage httpResponseMessage = client.PostAsync(searchUrl, form).Result;
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
            // 2026 site layout: <h2 class="h5 mb-2 result-title"><a class="..." href="/downloads/download/...">Name</a></h2>
            //                  ... <div class="result-text">description</div>
            // (the old layout used <dt class="result-title"> / <dd class="result-text">)
            string pattern = @"<h2[^>]*\bresult-title\b[^>]*>\s*<a[^>]*\bhref=""([^""]*)""[^>]*>(.*?)</a>.*?<div class=""result-text"">(.*?)</div>";
            var matches = Regex.Matches(html, pattern, RegexOptions.Singleline);

            ushort searchResultIndexId = 1;
            foreach (Match match in matches)
            {
                string imageName = WebUtility.HtmlDecode(Regex.Replace(match.Groups[2].Value, @"<.*?>", "")).Trim(); // e.g. "Lucid &amp; Mr. Lee" -> "Lucid & Mr. Lee"
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

        // form fields as the site's own search form sends them (Joomla com_search)
        private FormUrlEncodedContent BuildCommodoreSoftwareSearchForm(string searchTerm, string token)
        {
            var fields = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("searchword", searchTerm),
                new KeyValuePair<string, string>("ordering", "popular"), // newest, oldest, popular, alpha, category
                new KeyValuePair<string, string>("Search", ""),
                new KeyValuePair<string, string>("task", "search"),
                new KeyValuePair<string, string>("reset", ""),
                new KeyValuePair<string, string>("searchphrase", "all"),
                new KeyValuePair<string, string>("limit", "0") // all
            };

            if (!string.IsNullOrEmpty(token))
            {
                fields.Add(new KeyValuePair<string, string>(token, "1"));
            }

            return new FormUrlEncodedContent(fields);
        }

        private string BuildFullCommodoreSoftwarePath(string relativePath)
        {
            return this.Configuration.FloppyResolverSettings.CommodoreSoftware.BaseURL + relativePath;
        }
    }
}
