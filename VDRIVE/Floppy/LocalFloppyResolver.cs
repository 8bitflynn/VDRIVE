using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Floppy
{
    public class LocalFloppyResolver : IFloppyResolver
    {
        public LocalFloppyResolver(IConfiguration configuration, ILog logger)
        {
            this.Configuration = configuration;
            this.Logger = logger;
        }
        private IConfiguration Configuration;
        private ILog Logger;
        private FloppyInfo? InsertedFloppyInfo;

        FloppyInfo? IFloppyResolver.InsertFloppy(FloppyInfo floppyInfo)
        {
            this.Logger.LogMessage("Inserting floppy: " + new string(floppyInfo.ImagePath));
            this.InsertedFloppyInfo = new FloppyInfo() { ImagePath = floppyInfo.ImagePath };
            return this.InsertedFloppyInfo.Value; // should work for now
        }
        FloppyInfo? IFloppyResolver.EjectFloppy()
        {
            this.Logger.LogMessage(Logger is null ? "Ejecting floppy" : "Ejecting floppy: " + this.InsertedFloppyInfo?.ImagePath);
            this.InsertedFloppyInfo = null;
            return this.InsertedFloppyInfo;
        }
        FloppyInfo? IFloppyResolver.GetInsertedFloppyInfo()
        {
            return this.InsertedFloppyInfo;
        }

        SearchFloppyResponse IFloppyResolver.SearchFloppys(SearchFloppiesRequest searchFloppiesRequest)
        {
            this.Logger.LogMessage($"Searching floppy images for description '{new string(searchFloppiesRequest.Description)}' and media type '{searchFloppiesRequest.MediaType}'");
            SearchFloppyResponse searchFloppyResponse = new SearchFloppyResponse();
            List<FloppyInfo> floppyInfos = new List<FloppyInfo>();
            foreach (string searchPath in this.Configuration.SearchPaths)
            {
                string[]? extensions = (searchFloppiesRequest.MediaType != null ? searchFloppiesRequest.MediaType.Split(',') : null);
                IEnumerable<string> searchResults = this.TraverseFolder(searchPath, new string(searchFloppiesRequest.Description), extensions, true);
                if (searchResults != null)
                {
                    foreach (string searchResult in searchResults)
                    {
                        FloppyInfo floppyInfo = new FloppyInfo();                       
                        floppyInfo.ImagePath = searchResult.ToCharArray();
                        floppyInfo.ImagePathLength = (byte)floppyInfo.ImagePath.Length;
                        floppyInfo.Description = Path.GetFileNameWithoutExtension(searchResult)?.ToCharArray();
                        floppyInfo.DescriptionLengthLo = (byte)(floppyInfo.Description?.Length ?? 0);
                        floppyInfo.DescriptionLengthHi = (byte)((floppyInfo.Description?.Length ?? 0) >> 8);
                        floppyInfo.MediaType = Path.GetExtension(searchResult).TrimStart('.').ToLowerInvariant().ToCharArray();
                        floppyInfo.MediaTypeLength = (byte)floppyInfo.MediaType.Length;
                        // TODO: validate the FloppyInfo fields are within limits
                        floppyInfos.Add(floppyInfo);                        
                    }                   
                }
            }
            searchFloppyResponse.SearchResults = floppyInfos.ToArray();
            searchFloppyResponse.ResponseCode = 0xff; // success for now
            searchFloppyResponse.ResultCount = (byte)searchFloppyResponse.SearchResults.Length;            

            this.Logger.LogMessage($"Found {searchFloppyResponse.SearchResults.Length} floppy images matching description '{new string(searchFloppiesRequest.Description)}' and media type '{searchFloppiesRequest.MediaType}'");
            return searchFloppyResponse;
        }

        private IEnumerable<string> TraverseFolder(string root, string description, IEnumerable<string> extensions = null, bool recurse = true)
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
