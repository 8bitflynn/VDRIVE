using System.Collections.Concurrent;
using System.Numerics;
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
        private FloppyInfo? InsertedFloppyInfo; // info returned to C64
        private FloppyPointer? SelectedFloppyPointer; // join to FloppyInfo.Id for long path
        private List<FloppyPointer> FloppyPointers = new List<FloppyPointer>();
       
        FloppyInfo? IFloppyResolver.InsertFloppy(FloppyInfo floppyInfo)
        {
            this.Logger.LogMessage("Inserting floppy: " + new string(floppyInfo.ImageName));
            this.InsertedFloppyInfo = floppyInfo;
            this.SelectedFloppyPointer = this.FloppyPointers.FirstOrDefault(fp => fp.Id == (floppyInfo.IdLo | (floppyInfo.IdHi  << 8)));
            return this.InsertedFloppyInfo.Value; // should work for now
        }
        FloppyInfo? IFloppyResolver.EjectFloppy()
        {
            this.Logger.LogMessage(Logger is null ? "Ejecting floppy" : "Ejecting floppy: " + this.InsertedFloppyInfo?.ImageName);
            this.InsertedFloppyInfo = null;
            this.SelectedFloppyPointer = null;
            return this.InsertedFloppyInfo;
        }
        
        FloppyInfo? IFloppyResolver.GetInsertedFloppyInfo()
        {
            return this.InsertedFloppyInfo;
        }

        public FloppyPointer? GetInsertedFloppyPointer()
        {
            return this.SelectedFloppyPointer;
        }

        SearchFloppyResponse IFloppyResolver.SearchFloppys(SearchFloppiesRequest searchFloppiesRequest)
        {
            this.Logger.LogMessage($"Searching floppy images for description '{new string(searchFloppiesRequest.SearchTerm)}' and media type '{searchFloppiesRequest.MediaType}'");

            // clear last results
            this.FloppyPointers.Clear();

            ushort searchResultIndex = 0;

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
                        
                        // results stored in resolver for later use
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
