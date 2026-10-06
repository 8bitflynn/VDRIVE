using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Floppy.Impl
{
    public class CommodoreSoftwareFloppyResolver : RemoteFloppyResolverBase, IFloppyResolver
    {
        // All disk images from the last download (Disk 1, Disk 2, ...), so the website can get the whole set as a ZIP.
        // The C64 still only mounts the primary disk (see ResolvePrimaryDisk).
        public IReadOnlyList<string> InsertedDiskSet { get; private set; } = Array.Empty<string>();

        public CommodoreSoftwareFloppyResolver(IConfiguration configuration, ILogger logger)
        {
            Configuration = configuration;
            Logger = logger;
        }

        public override FloppyInfo InsertFloppy(FloppyIdentifier floppyIdentifier)
        {
            this.InsertedDiskSet = Array.Empty<string>();
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

                            var allowed = Configuration.FloppyResolverSettings.CommodoreSoftware.MediaExtensionsAllowed
                                .Select(ext => ext.ToLowerInvariant()).ToHashSet();
                            this.InsertedDiskSet = extractedFilePaths
                                .Where(path => allowed.Contains(Path.GetExtension(path).ToLowerInvariant()))
                                .OrderBy(path => path == fullFilePath ? 0 : 1)   // the disk a C64 would mount comes first (the browser autostarts it)
                                .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                                .ToList();

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
                    IEnumerable<FloppyInfo> floppyInfos = ScrapeResults(html, client);

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

        private IEnumerable<FloppyInfo> ScrapeResults(string html, HttpClient client)
        {
            List<FloppyInfo> floppyInfos = new List<FloppyInfo>();
            CategoryLookup categoryLookup = new CategoryLookup { Left = MaxCategoryLookupsPerSearch };

            // Match each result block
            // 2026 site layout: <h2 class="h5 mb-2 result-title"><a class="..." href="/downloads/download/...">Name</a></h2>
            //                  ... <div class="result-text">description</div>
            // (the old layout used <dt class="result-title"> / <dd class="result-text">)
            string pattern = @"<h2[^>]*\bresult-title\b[^>]*>\s*<a[^>]*\bhref=""([^""]*)""[^>]*>(.*?)</a>.*?<div class=""result-text"">(.*?)</div>";
            var matches = Regex.Matches(html, pattern, RegexOptions.Singleline);

            PrefetchCategories(client, matches.Cast<Match>().Select(m => m.Groups[1].Value.Trim()), categoryLookup);

            ushort searchResultIndexId = 1;
            foreach (Match match in matches)
            {
                string imageName = WebUtility.HtmlDecode(Regex.Replace(match.Groups[2].Value, @"<.*?>", "")).Trim(); // e.g. "Lucid &amp; Mr. Lee" -> "Lucid & Mr. Lee"
                if (Configuration.FloppyResolverSettings.CommodoreSoftware.IgnoredSearchKeywords.Any(ir => imageName.ToLower().Contains(ir.ToLower())))
                {
                    // skip this result as it is ignored
                    continue;
                }

                // only C64 software: the site also lists C128, VIC-20, Amiga, PC tools and reference material (manuals)
                string href = match.Groups[1].Value.Trim();
                int rootCategoryId = GetRootCategoryId(client, href, categoryLookup);
                if (rootCategoryId > 0 && rootCategoryId != C64RootCategoryId)
                {
                    Logger.LogMessage($"Skipping '{imageName}' (category branch {rootCategoryId}, not C64)", VDRIVE_Contracts.Enums.LogSeverity.Verbose);
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

        // ---------------------------------------------------------------------------------------------
        // Category filter. Download links look like /downloads/download/50-assemblers/1360-assblaster-v3-3.
        // Categories nest (50 Assemblers > 49 Assembler > 7 Programming > 811 Commodore 64 Software), and each
        // category page has a "Go a Level up" link, so follow those to the top-level branch once per category
        // and remember the answer in a small file. Unknown / failed lookups return -1 and the result is kept.
        // ---------------------------------------------------------------------------------------------
        private const int C64RootCategoryId = 811;          // "Commodore 64 Software" on commodore.software/downloads
        private const int MaxCategoryLookupsPerSearch = 15; // page fetches; keeps a first search light on the site
        private const int CategoryLookupThreads = 4;        // category pages fetched at the same time
        private static readonly ConcurrentDictionary<int, int> CategoryRoots = new ConcurrentDictionary<int, int>();
        private static readonly object CategoryFileLock = new object();
        private static bool categoryFileLoaded;

        private string CategoryFilePath =>
            Path.Combine(this.Configuration.TempPath ?? Path.GetTempPath(), this.Configuration.TempFolder ?? "vdrive_tmp", "cs_category_roots.txt");

        // one per search: the page budget, plus each category page fetched at most once (two results that share
        // an unknown parent wait for the same fetch instead of both downloading it)
        private sealed class CategoryLookup
        {
            public int Left;
            public int Fetched;
            public readonly ConcurrentDictionary<int, Lazy<CategoryParent>> Parents = new ConcurrentDictionary<int, Lazy<CategoryParent>>();
        }

        private sealed class CategoryParent
        {
            public int Id;      // 0 = no parent, this is a top-level branch
            public string Slug;
        }

        private static Match MatchCategory(string downloadHref) =>
            Regex.Match(downloadHref ?? "", @"/downloads/download/(\d+)-([^/""]+)/");

        // look up the unknown categories a few at a time before the results are filtered, in result order so the
        // budget goes to the top results first; the filter loop then only reads the cache
        private void PrefetchCategories(HttpClient client, IEnumerable<string> downloadHrefs, CategoryLookup lookup)
        {
            LoadCategoryFile();

            var unknown = downloadHrefs
                .Select(MatchCategory)
                .Where(m => m.Success)
                .Select(m => (Id: int.Parse(m.Groups[1].Value), Slug: m.Groups[2].Value))
                .Where(c => !CategoryRoots.ContainsKey(c.Id))
                .GroupBy(c => c.Id)
                .Select(g => g.First())
                .ToList();

            if (unknown.Count == 0)
            {
                return;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Parallel.ForEach(
                Partitioner.Create(unknown, EnumerablePartitionerOptions.NoBuffering),
                new ParallelOptions { MaxDegreeOfParallelism = CategoryLookupThreads },
                c => GetRootCategoryId(client, c.Id, c.Slug, lookup));

            Logger.LogMessage($"Commodore.Software categories: {unknown.Count} unknown, {lookup.Fetched} pages fetched in {stopwatch.ElapsedMilliseconds}ms", VDRIVE_Contracts.Enums.LogSeverity.Verbose);
        }

        private int GetRootCategoryId(HttpClient client, string downloadHref, CategoryLookup lookup)
        {
            Match category = MatchCategory(downloadHref);
            if (!category.Success)
            {
                return -1;
            }

            return GetRootCategoryId(client, int.Parse(category.Groups[1].Value), category.Groups[2].Value, lookup);
        }

        private int GetRootCategoryId(HttpClient client, int current, string currentSlug, CategoryLookup lookup)
        {
            LoadCategoryFile();
            List<int> chain = new List<int>();

            for (int depth = 0; depth < 10; depth++)
            {
                if (CategoryRoots.TryGetValue(current, out int knownRoot))
                {
                    RememberRoot(chain, knownRoot);
                    return knownRoot;
                }

                int categoryId = current;
                string slug = currentSlug;
                CategoryParent parent = lookup.Parents
                    .GetOrAdd(categoryId, _ => new Lazy<CategoryParent>(() => FetchCategoryParent(client, categoryId, slug, lookup)))
                    .Value;

                if (parent == null)
                {
                    return -1; // out of budget or the lookup failed: keep the result
                }

                chain.Add(current);

                if (parent.Id == 0)
                {
                    RememberRoot(chain, current); // no parent category: this is the top-level branch
                    return current;
                }

                current = parent.Id;
                currentSlug = parent.Slug;
            }

            return -1;
        }

        private CategoryParent FetchCategoryParent(HttpClient client, int categoryId, string slug, CategoryLookup lookup)
        {
            if (Interlocked.Decrement(ref lookup.Left) < 0)
            {
                return null;
            }
            Interlocked.Increment(ref lookup.Fetched);

            string page;
            try
            {
                page = client.GetStringAsync(BuildFullCommodoreSoftwarePath($"/downloads/category/{categoryId}-{slug}")).Result;
            }
            catch (Exception ex)
            {
                Logger.LogMessage($"Category lookup failed for {categoryId}: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Verbose);
                return null;
            }

            if (!page.Contains("jd_top_navi"))
            {
                return null; // not a jDownloads category page (error page, layout change): don't guess
            }

            // <a href="/downloads/category/7-programming" title="Go a Level up"> (top-level categories go up to /downloads)
            Match up = Regex.Match(page, @"<a(?=[^>]*title=""Go a Level up"")[^>]*href=""/downloads/category/(\d+)-([^""/]*)""", RegexOptions.IgnoreCase);
            return up.Success
                ? new CategoryParent { Id = int.Parse(up.Groups[1].Value), Slug = up.Groups[2].Value }
                : new CategoryParent { Id = 0, Slug = "" };
        }

        private void RememberRoot(List<int> chain, int root)
        {
            if (chain.Count == 0)
            {
                return;
            }

            foreach (int categoryId in chain)
            {
                CategoryRoots[categoryId] = root;
            }

            try
            {
                lock (CategoryFileLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(CategoryFilePath));
                    File.AppendAllLines(CategoryFilePath, chain.Select(categoryId => $"{categoryId}={root}"));
                }
            }
            catch (Exception ex)
            {
                Logger.LogMessage($"Could not save category map: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Warning);
            }
        }

        private void LoadCategoryFile()
        {
            if (categoryFileLoaded)
            {
                return;
            }

            lock (CategoryFileLock)
            {
                if (categoryFileLoaded)
                {
                    return;
                }
                categoryFileLoaded = true;

                try
                {
                    if (!File.Exists(CategoryFilePath))
                    {
                        return;
                    }

                    foreach (string line in File.ReadAllLines(CategoryFilePath))
                    {
                        string[] parts = line.Split('=');
                        if (parts.Length == 2 && int.TryParse(parts[0], out int categoryId) && int.TryParse(parts[1], out int root))
                        {
                            CategoryRoots[categoryId] = root;
                        }
                    }
                    Logger.LogMessage($"Loaded {CategoryRoots.Count} Commodore.Software categories from {CategoryFilePath}");
                }
                catch (Exception ex)
                {
                    Logger.LogMessage($"Could not read category map: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Warning);
                }
            }
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
