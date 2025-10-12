using System.Collections.Concurrent;
using System.Numerics;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Floppy
{
    public class LocalFloppyResolver : FloppyResolverBase, IFloppyResolver
    {
        public LocalFloppyResolver(IConfiguration configuration, ILog logger)
        {
            this.Configuration = configuration;
            this.Logger = logger;
        }

        public override SearchFloppyResponse SearchFloppys(SearchFloppiesRequest searchFloppiesRequest)
        {
            this.Logger.LogMessage($"Searching floppy images for description '{new string(searchFloppiesRequest.SearchTerm)}' and media type '{searchFloppiesRequest.MediaType}'");

            // clear last search results
            this.FloppyInfos.Clear();
            this.FloppyPointers.Clear();

            ushort searchResultIndex = 1;

            SearchFloppyResponse searchFloppyResponse = new SearchFloppyResponse();
            List<FloppyInfo> floppyInfos = new List<FloppyInfo>();
            foreach (string searchPath in this.Configuration.SearchPaths)
            {
                string[]? extensions = (searchFloppiesRequest.MediaType != null ? searchFloppiesRequest.MediaType.Split(',') : null);
                IEnumerable<string> searchResults = this.TraverseFolder(searchPath, new string(searchFloppiesRequest.SearchTerm), extensions, true);
                if (searchResults != null)
                {
                    foreach (string searchResult in searchResults)
                    { 
                        // info returned to C64
                        FloppyInfo floppyInfo = new FloppyInfo();
                        floppyInfo.IdLo = (byte)searchResultIndex;
                        floppyInfo.IdHi = (byte)(searchResultIndex >> 8);
                        floppyInfo.ImageName = Path.GetFileName(searchResult).ToCharArray();
                        floppyInfo.ImageNameLength = (byte)floppyInfo.ImageName.Length;
                        floppyInfo.Description = Path.GetFileNameWithoutExtension(searchResult).ToCharArray(); // just use name without extension for now
                        floppyInfo.DescriptionLength = (byte)(floppyInfo.Description?.Length ?? 0);
                        // TODO: validate the FloppyInfo fields are within limits

                        // info stored in resolver with longs paths
                        FloppyPointer floppyPointer = new FloppyPointer();
                        floppyPointer.Id = searchResultIndex;
                        floppyPointer.ImagePath = searchResult;

                        floppyInfos.Add(floppyInfo);   
                        
                        // results stored in resolver for lookup if "inserted"
                        this.FloppyInfos.Add(floppyInfo);
                        this.FloppyPointers.Add(floppyPointer);

                        searchResultIndex++;
                    }                   
                }
            }
            searchFloppyResponse.SearchResults = floppyInfos.ToArray();
            searchFloppyResponse.ResponseCode = 0xff; // success for now
            searchFloppyResponse.ResultCount = (byte)searchFloppyResponse.SearchResults.Length;            

            this.Logger.LogMessage($"Found {searchFloppyResponse.SearchResults.Length} floppy images matching description '{new string(searchFloppiesRequest.SearchTerm)}' and media type '{searchFloppiesRequest.MediaType}'");
            return searchFloppyResponse;
        }

        private List<string> TraverseFolder(string root, string description, IEnumerable<string> extensions = null, bool recurse = true)
        {
            var results = new List<string>();

            if (string.IsNullOrWhiteSpace(root)) return results;
            if (!Directory.Exists(root)) return results;

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

                if (extMatches && descMatches) results.Add(fi.FullName);
            }

            if (!recurse) return results;

            IEnumerable<string> subDirs;
            try
            {
                subDirs = Directory.EnumerateDirectories(root);
            }
            catch (UnauthorizedAccessException) { return results; }
            catch (DirectoryNotFoundException) { return results; }
            catch (IOException) { return results; }

            foreach (var sub in subDirs)
            {
                try
                {
                    var childMatches = TraverseFolder(sub, description, extSet, recurse: true);
                    if (childMatches != null && childMatches.Count > 0) results.AddRange(childMatches);
                }
                catch
                {
                    // ignore directory-specific errors and continue
                }
            }

            return results;
        }      
    }
}
