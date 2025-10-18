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

        public override SearchFloppyResponse SearchFloppys(SearchFloppiesRequest searchFloppiesRequest, out FloppyInfo[] foundFloppyInfos)
        {
            this.Logger.LogMessage($"Searching floppy images for description '{new string(searchFloppiesRequest.SearchTerm)}' and media type '{new string (searchFloppiesRequest.MediaType)}'");

            // clear last search results
            this.FloppyInfos.Clear();
            this.FloppyPointers.Clear();

            string[] mediaTypes = null;
            string mediaTypeCSV = new string(searchFloppiesRequest.MediaType.TakeWhile(c => c != '\0').ToArray());
            if (!string.IsNullOrWhiteSpace(mediaTypeCSV))
            {
                mediaTypes = mediaTypeCSV.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            else
            {
                mediaTypes = this.Configuration.MediaExtensionAllowed.Split(',');
            }
            string searchTerm = new string(searchFloppiesRequest.SearchTerm.TakeWhile(c => c != '\0').ToArray());
            ushort searchResultIndexId = 1; // used to select floppy on C64 side 

            List<FloppyInfo> floppyInfos = new List<FloppyInfo>();
            foreach (string searchPath in this.Configuration.SearchPaths)
            {
                IEnumerable<string> searchResults = this.TraverseFolder(searchPath, searchTerm, mediaTypes, true);
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
                        // imageName is shown to user but only the ID is important 
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
                        this.FloppyInfos.Add(floppyInfo);
                        this.FloppyPointers.Add(floppyPointer);

                        searchResultIndexId++;
                    }                   
                }
            }
            foundFloppyInfos = floppyInfos.Take(this.Configuration.MaxSearchResults).ToArray();

            SearchFloppyResponse searchFloppyResponse = this.BuildSearchFloppyResponse(4096, (floppyInfos.Count() > 0 ? (byte)0xff : (byte)0x04), (byte)foundFloppyInfos.Count()); 

            this.Logger.LogMessage($"Found {foundFloppyInfos.Length} floppy images matching search term '{new string(searchFloppiesRequest.SearchTerm)}' and media type '{new string (searchFloppiesRequest.MediaType)}'");
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
