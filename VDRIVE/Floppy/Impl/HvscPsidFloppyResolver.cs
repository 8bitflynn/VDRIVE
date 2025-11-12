using System.Data.SqlTypes;
using System.Net;
using System.Text.RegularExpressions;
using VDRIVE_Contracts.Enums;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Floppy.Impl
{
    public class HvscPsidFloppyResolver : RemoteFloppyResolverBase, IFloppyResolver
    {
        public HvscPsidFloppyResolver(IConfiguration configuration, ILogger logger, IProcessRunner processRunner)
        {
            Configuration = configuration;
            Logger = logger;
            this.ProcessRunner = processRunner;
        }
        private IProcessRunner ProcessRunner;

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
                string searchUrl = BuildHcsvSearchUrl(searchTerm);

                HttpResponseMessage httpResponseMessage = client.GetAsync(searchUrl).Result;
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
            string pattern = @"\{""id"":(\d+),""title"":""([^""]+)"",""author"":""([^""]+)"",""released"":""([^""]+)""\}"
;
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
                    string downloadPageUrl = BuildHsvDownloadUrl(InsertedFloppyPointer.ImagePath);
                    HttpResponseMessage httpResponseMessage = client.GetAsync(downloadPageUrl).Result;
                    byte[] rawBytes = httpResponseMessage.Content.ReadAsByteArrayAsync().Result;

                    if (httpResponseMessage.IsSuccessStatusCode)
                    {
                        // Save raw SID bytes to disk
                        string rawSidPath = Path.Combine(this.Configuration.TempPath, this.Configuration.TempFolder, "input.sid");
                        File.WriteAllBytes(rawSidPath, rawBytes);

                        // Define output PRG path
                        string prgOutputPath = Path.Combine(this.Configuration.TempPath, this.Configuration.TempFolder, "converted.prg");

                        // Build minimal psid64 arguments
                        string arguments = $"-c -i 1 -o \"{prgOutputPath}\" \"{rawSidPath}\"";


                        RunProcessParameters runProcessParameters = new RunProcessParameters
                        {
                            ImagePath = @"C:\Programming\Tool\psid64-1.3-win32",
                            ExecutablePath = Path.Combine(@"C:\Programming\Tool\psid64-1.3-win32", "psid64.exe"),
                            Arguments = arguments,
                            LockType = LockType.Write,
                            LockTimeoutSeconds = this.Configuration.StorageAdapterSettings.LockTimeoutSeconds
                        };

                        RunProcessResult runProcessResult = this.ProcessRunner.RunProcess(runProcessParameters);


                        this.InsertedFloppyInfo.ImageName = Path.GetFileName(prgOutputPath).ToCharArray();
                        this.InsertedFloppyPointer.ImagePath = prgOutputPath; // update to disk image extracted file path   

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

        private string BuildHcsvSearchUrl(string searchTerm)
        {
            return $"https://www.hvsc.c64.org/api/v1/sids?q={Uri.EscapeDataString(searchTerm)}";
        }


        private string BuildHsvDownloadUrl(string downloadId)
        {
            //https://www.hvsc.c64.org/download/sids/78963
            return $"https://www.hvsc.c64.org/download/sids/{downloadId}";
        }
    }
}
