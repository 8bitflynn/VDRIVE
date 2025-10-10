using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Floppy
{
    public class LocalFloppyResolver : IFloppyResolver
    {
        public LocalFloppyResolver(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }
        private IConfiguration Configuration;
        private FloppyInfo? InsertedFloppyInfo;

        FloppyInfo? IFloppyResolver.InsertFloppy(FloppyInfo floppyInfo)
        {
            this.InsertedFloppyInfo = new FloppyInfo() { ImagePath = floppyInfo.ImagePath };
            return this.InsertedFloppyInfo.Value; // should work for now
        }
        FloppyInfo? IFloppyResolver.EjectFloppy()
        {
            this.InsertedFloppyInfo = null;
            return this.InsertedFloppyInfo;
        }
        FloppyInfo? IFloppyResolver.GetInsertedFloppyInfo()
        {
            return this.InsertedFloppyInfo;
        }

        SearchFloppyResponse IFloppyResolver.SearchFloppys(SearchFloppiesRequest searchFloppiesRequest)
        {
            SearchFloppyResponse searchFloppyResponse = new SearchFloppyResponse();
            foreach (string searchPath in this.Configuration.SearchPaths)
            {
                IEnumerable<string> searchResults = this.TraverseFolder(searchPath, searchFloppiesRequest.Description, searchFloppiesRequest.MediaType.Split(','), true);
                if (searchResults != null)
                {
                    foreach (string searchResult in searchResults)
                    {
                        FloppyInfo floppyInfo = new FloppyInfo();
                        floppyInfo.ImagePath = $"{searchResult}";
                        floppyInfo.Description = Path.GetFileNameWithoutExtension(searchResult);
                        floppyInfo.MediaType = Path.GetExtension(searchResult).TrimStart('.').ToLowerInvariant();
                        floppyInfo.WriteProtected = (new FileInfo(searchResult).IsReadOnly) ? "true" : "false";
                        searchFloppyResponse.SearchResults.Add(floppyInfo);
                    }                  
                }
            }

            return searchFloppyResponse;
        }

        public IEnumerable<string> TraverseFolder(string root, string description, IEnumerable<string> extensions = null, bool recurse = true)
        {
            if (string.IsNullOrWhiteSpace(root)) yield break;
            if (!Directory.Exists(root)) yield break;

            // Normalize extensions to a set of lower-case strings with leading dot
            HashSet<string> extSet = null;
            if (extensions != null)
            {
                extSet = new HashSet<string>(
                    extensions
                        .Where(e => !string.IsNullOrWhiteSpace(e))
                        .Select(e => e.StartsWith(".") ? e.ToLowerInvariant().Trim() : "." + e.ToLowerInvariant().Trim())
                );
                if (extSet.Count == 0) extSet = null;
            }

            string desc = description ?? string.Empty;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root);
            }
            catch (UnauthorizedAccessException) { yield break; }
            catch (DirectoryNotFoundException) { yield break; }
            catch (IOException) { yield break; }

            foreach (var file in files)
            {
                FileInfo fi;
                try { fi = new FileInfo(file); }
                catch { continue; }

                var name = fi.Name;
                var fileExt = fi.Extension.ToLowerInvariant();

                bool extMatches = extSet == null || extSet.Contains(fileExt);
                bool descMatches = string.IsNullOrEmpty(desc) || name.IndexOf(desc, StringComparison.OrdinalIgnoreCase) >= 0;

                if (extMatches && descMatches) yield return fi.FullName;
            }

            if (!recurse) yield break;

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(root);
            }
            catch (UnauthorizedAccessException) { yield break; }
            catch (DirectoryNotFoundException) { yield break; }
            catch (IOException) { yield break; }

            foreach (var sub in subDirs)
            {
                foreach (var match in TraverseFolder(sub, description, extSet, recurse: true))
                    yield return match;
            }
        }
    }
}
