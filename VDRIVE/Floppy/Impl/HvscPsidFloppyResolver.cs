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
            this.Configuration = configuration;
            this.Logger = logger;
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

            Logger.LogMessage($"Searching High Voltage Sid Collection for description '{searchTerm}' and media type '{mediaTypeCSV}'");

            // the local copy first (HvscPsid.LocalPath): faster, and works when the web is down. Nothing there
            // (a tune newer than the copy, or the index still building) - the web, as before.
            List<VDRIVE.Util.HvscIndex.Entry> index = VDRIVE.Util.HvscIndex.Get(LocalPath, Logger);
            if (index != null)
            {
                List<VDRIVE.Util.HvscIndex.Entry> hits = VDRIVE.Util.HvscIndex.Search(index, searchTerm);
                if (hits.Count > 0)
                {
                    Logger.LogMessage($"HVSC: {hits.Count} local result(s) for '{searchTerm}'");
                    ushort localId = 1;
                    List<FloppyInfo> localInfos = hits.Select(e => AddResult(e.Title, e.Author, e.Path, localId++)).ToList();
                    SearchFloppyResponse localResponse = BuildSearchFloppyResponse(0x1000, (byte)0xff, (byte)localInfos.Count);
                    foundFloppyInfos = localInfos.Take(Configuration.MaxSearchResults).ToArray();
                    localResponse.ResultCount = (byte)foundFloppyInfos.Length;
                    return localResponse;
                }
            }

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
            string pattern = @"\{""id"":(\d+),""title"":""([^""]+)"",""author"":""([^""]+)"",""released"":""([^""]+)""\}";
            
            var matches = Regex.Matches(html, pattern, RegexOptions.Singleline);

            ushort searchResultIndexId = 1;
            foreach (Match match in matches)
            {
                string title = JsonText(match.Groups[2].Value);
                string author = JsonText(match.Groups[3].Value);
                string released = JsonText(match.Groups[4].Value);

                // a web result that is in the local copy loads from it (no download); else by its web id
                string imagePath = VDRIVE.Util.HvscIndex.Find(LocalPath, title, author, released, Logger) ?? match.Groups[1].Value.Trim();
                floppyInfos.Add(AddResult(title, author, imagePath, searchResultIndexId));
                searchResultIndexId++;
            }

            return floppyInfos;
        }

        private string LocalPath => this.Configuration.FloppyResolverSettings.HvscPsid.LocalPath;

        // a JSON string's text (é -> é), as the SID's own header has it
        private static string JsonText(string raw)
        {
            try { return System.Text.Json.JsonSerializer.Deserialize<string>("\"" + raw + "\"").Trim(); }
            catch (Exception) { return raw.Trim(); }
        }

        // one result: "TITLE - AUTHOR" (as the C64 and the website cards know it) and where it comes from -
        // a web id, or a local file's full path
        private FloppyInfo AddResult(string title, string author, string imagePath, ushort searchResultIndexId)
        {
            string imageName = title + " - " + author;

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

            // floppy pointers hold Id and long path and are looked up when "inserted"
            FloppyPointer floppyPointer = new FloppyPointer();
            floppyPointer.Id = searchResultIndexId;
            floppyPointer.ImagePath = imagePath;

            // results stored in resolver for lookup if "inserted"
            FloppyInfos.Add(floppyInfo);
            FloppyPointers.Add(floppyPointer);

            return floppyInfo;
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
                    // a local file (full path, HvscIndex) or a web id
                    bool local = Path.IsPathRooted(InsertedFloppyPointer.ImagePath);
                    HttpResponseMessage httpResponseMessage = local ? null : client.GetAsync(BuildHsvDownloadUrl(InsertedFloppyPointer.ImagePath)).Result;
                    byte[] rawBytes = local ? File.ReadAllBytes(InsertedFloppyPointer.ImagePath) : httpResponseMessage.Content.ReadAsByteArrayAsync().Result;
                    if (local) Logger.LogMessage($"HVSC: local {InsertedFloppyPointer.ImagePath}");

                    if (local || httpResponseMessage.IsSuccessStatusCode)
                    {
                        string fullPath = VDRIVE.Util.WorkFolders.Ensure(this.Configuration, this.WorkFolder);
                        if (!Directory.Exists(fullPath))
                            Directory.CreateDirectory(fullPath);

                        // Save raw SID bytes to disk for psid64
                        string rawSidPath = Path.Combine(fullPath, "raw.sid");
                        File.WriteAllBytes(rawSidPath, rawBytes);

                        // Define output PRG path
                        string prgOutputPath = Path.Combine(fullPath, "c64sid.prg");

                        // Build minimal psid64 arguments
                        string arguments = $"{this.Configuration.FloppyResolverSettings.HvscPsid.Arguments} -o \"{prgOutputPath}\" \"{rawSidPath}\"";

                        RunProcessParameters runProcessParameters = new RunProcessParameters
                        {
                            ImagePath = @"C:\Programming\Tool\psid64-1.3-win32",
                            ExecutablePath = this.Configuration.FloppyResolverSettings.HvscPsid.ExecutablePath,
                            Arguments = arguments,
                            LockType = LockType.Write,
                            LockTimeoutSeconds = this.Configuration.StorageAdapterSettings.LockTimeoutSeconds
                        };

                        RunProcessResult runProcessResult = this.ProcessRunner.RunProcess(runProcessParameters);

                        // FIXME: hack to get extension in name
                        this.InsertedFloppyInfo.ImageName = (new string(floppyInfo.ImageName).TrimEnd('\0')).ToCharArray();
                        this.InsertedFloppyInfo.ImageNameLength = (byte)this.InsertedFloppyInfo.ImageName.Length;
                        this.InsertedFloppyPointer.ImagePath = prgOutputPath; // update to disk image extracted file path   
                    }
                }
            }
            catch (Exception exception)
            {
                Logger.LogMessage("Failed to insert floppy: " + exception.Message);
                return default;
            }

            return this.InsertedFloppyInfo;
        }

        private string BuildHcsvSearchUrl(string searchTerm)
        {
            return $"{this.Configuration.FloppyResolverSettings.HvscPsid.BaseURL}/api/v1/sids?q={Uri.EscapeDataString(searchTerm)}";
        }


        private string BuildHsvDownloadUrl(string downloadId)
        {
            return $"{this.Configuration.FloppyResolverSettings.HvscPsid.BaseURL}/download/sids/{downloadId}";
        }
    }
}
