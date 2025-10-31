using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Floppy.Impl
{
    public class LocalFloppyResolver : FloppyResolverBase, IFloppyResolver
    {
        public LocalFloppyResolver(IConfiguration configuration, ILogger logger)
        {
            Configuration = configuration;
            Logger = logger;
        }

        public override SearchFloppyResponse SearchFloppys(SearchFloppiesRequest searchFloppiesRequest, out FloppyInfo[] foundFloppyInfos)
        {
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

            Logger.LogMessage($"Searching floppy images for description '{searchTerm}' and media type '{mediaTypeCSV}'");

            // clear last search results
            ClearSearchResults();

            ushort searchResultIndexId = 1; // sequence used to select floppy on C64 side 
            List<FloppyInfo> floppyInfos = new List<FloppyInfo>();
            foreach (string searchPath in Configuration.FloppyResolverSettings.Local.SearchPaths)
            {
                IEnumerable<string> searchResults = TraverseFolder(searchPath, searchTerm, Configuration.FloppyResolverSettings.Local.MediaExtensionsAllowed, Configuration.FloppyResolverSettings.Local.EnableRecursiveSearch);
                if (searchResults != null)
                {
                    foreach (string searchResult in searchResults)
                    {
                        // info returned to C64
                        FloppyInfo floppyInfo = new FloppyInfo();
                        floppyInfo.IdLo = (byte)searchResultIndexId;
                        floppyInfo.IdHi = (byte)(searchResultIndexId >> 8);

                        string imageName = Path.GetFileName(searchResult);
                        imageName = imageName.Length > 64 ? imageName.Substring(0, 64) : imageName; // truncate if needed

                        // TODO: map to PETSCII
                        // imageName shown to user but only sequential id used to select
                        // so show the best case description
                        imageName = imageName.Replace('_', '-'); // shows a back arrow on C64

                        floppyInfo.ImageNameLength = (byte)imageName.Length;
                        floppyInfo.ImageName = new char[64];
                        imageName.ToUpper().ToCharArray().CopyTo(floppyInfo.ImageName, 0);

                        // info stored in resolver with longs paths
                        FloppyPointer floppyPointer = new FloppyPointer();
                        floppyPointer.Id = searchResultIndexId;
                        floppyPointer.ImagePath = searchResult;

                        floppyInfos.Add(floppyInfo);

                        // results stored in resolver for lookup if "inserted"
                        FloppyInfos.Add(floppyInfo);
                        FloppyPointers.Add(floppyPointer);

                        searchResultIndexId++;
                    }
                }
            }
            // out param to return floppy info array
            foundFloppyInfos = floppyInfos.Take(Configuration.MaxSearchResults).ToArray();

            SearchFloppyResponse searchFloppyResponse = BuildSearchFloppyResponse(4096, floppyInfos.Count() > 0 ? (byte)0xff : (byte)0x04, (byte)foundFloppyInfos.Count());
            Logger.LogMessage($"Found {foundFloppyInfos.Length} floppy images matching search term '{searchTerm}' and media type '{mediaTypeCSV}'");

            return searchFloppyResponse;
        }

        private List<string> TraverseFolder(string root, string description, IEnumerable<string> extensions = null, bool recurse = true)
        {
            Logger.LogMessage($"Searching {root} for floppy images for description '{description}'");

            var results = new List<string>();

            if (string.IsNullOrWhiteSpace(root)) return results;
            if (!Directory.Exists(root)) return results;
            HashSet<string> extSet = null;
            if (extensions != null)
            {
                extSet = new HashSet<string>(
                    extensions.Where(e => !string.IsNullOrWhiteSpace(e))
                              .Select(e => e.StartsWith(".") ? e.ToLowerInvariant().Trim() : "." + e.ToLowerInvariant().Trim()),
                    StringComparer.OrdinalIgnoreCase);
                if (extSet.Count == 0) extSet = null;
            }

            string desc = description ?? string.Empty;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root);
            }
            catch (UnauthorizedAccessException) { return results; }
            catch (DirectoryNotFoundException) { return results; }
            catch (IOException) { return results; }

            foreach (var file in files)
            {
                FileInfo fi;
                try { fi = new FileInfo(file); }
                catch { continue; }

                var name = fi.Name;
                var fileExt = fi.Extension.ToLowerInvariant();

                bool extMatches = extSet == null || extSet.Contains(fileExt);
                bool descMatches = string.IsNullOrEmpty(desc) || name.IndexOf(desc, StringComparison.OrdinalIgnoreCase) >= 0;

                if (extMatches && descMatches)
                    results.Add(fi.FullName);
            }

            if (!recurse)
                return results;

            if (results.Count >= this.Configuration.MaxSearchResults)
                return results;

            IEnumerable<string> subDirectories;
            try
            {
                subDirectories = Directory.EnumerateDirectories(root);
            }
            catch (UnauthorizedAccessException) { return results; }
            catch (DirectoryNotFoundException) { return results; }
            catch (IOException) { return results; }

            foreach (string subDirectory in subDirectories)
            {
                try
                {
                    var childMatches = TraverseFolder(subDirectory, description, extSet, recurse: true);
                    if (childMatches != null && childMatches.Count > 0)
                    {
                        results.AddRange(childMatches);
                    }
                }
                catch
                {
                    // ignore directory-specific errors and continue
                }
            }

            return results.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
