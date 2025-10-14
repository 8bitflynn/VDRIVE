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
            this.Logger.LogMessage($"Searching floppy images for description '{new string(searchFloppiesRequest.SearchTerm)}' and media type '{searchFloppiesRequest.MediaType}'");

            // clear last search results
            this.FloppyInfos.Clear();
            this.FloppyPointers.Clear();

            string[] mediaTypes = null;
            string mediaTypeCSV = new string(searchFloppiesRequest.MediaType.TakeWhile(c => c != '\0').ToArray());
            if (string.IsNullOrWhiteSpace(mediaTypeCSV))
            {
                 mediaTypes = DefaultMediaExtensionsAllowed.Select(x => x.ToString()).ToArray();
            }
            else
            {
                mediaTypes = mediaTypeCSV.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            string searchTerm = new string(searchFloppiesRequest.SearchTerm.TakeWhile(c => c != '\0').ToArray());
            ushort searchResultIndex = 1;

            List<FloppyInfo> floppyInfos = new List<FloppyInfo>();
            foreach (string searchPath in this.Configuration.SearchPaths)
            {
                //string[]? extensions = mediaType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                IEnumerable<string> searchResults = this.TraverseFolder(searchPath, searchTerm, mediaTypes, true);
                if (searchResults != null)
                {
                    foreach (string searchResult in searchResults)
                    { 
                        // info returned to C64
                        FloppyInfo floppyInfo = new FloppyInfo();
                        floppyInfo.IdLo = (byte)searchResultIndex;
                        floppyInfo.IdHi = (byte)(searchResultIndex >> 8);

                        string imageName = Path.GetFileName(searchResult);
                        floppyInfo.ImageNameLength = (byte)imageName.Length;
                        floppyInfo.ImageName = new char[64];
                        imageName.ToUpper().ToCharArray().CopyTo(floppyInfo.ImageName, 0);                      

                        //string description = Path.GetFileNameWithoutExtension(searchResult); // just use name without extension for now
                       // floppyInfo.DescriptionLength = (byte)description.Length;
                       // floppyInfo.Description = new char[255];
                       // description.ToUpper().ToCharArray().CopyTo(floppyInfo.Description, 0);
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
            foundFloppyInfos = floppyInfos.ToArray();
           // searchFloppyResponse.ResponseCode = 0xff; // success for now
          //  searchFloppyResponse.ResultCount = (byte)floppyInfos.Count;

            SearchFloppyResponse searchFloppyResponse = this.BuildSearchFloppyResponse(4096, (floppyInfos.Count() > 0 ? (byte)0xff : (byte)0x04), (byte)floppyInfos.Count()); 




            this.Logger.LogMessage($"Found {foundFloppyInfos.Length} floppy images matching search term '{new string(searchFloppiesRequest.SearchTerm)}' and media type '{searchFloppiesRequest.MediaType}'");
            return searchFloppyResponse;
        }

        protected SearchFloppyResponse BuildSearchFloppyResponse(ushort destPtr, byte responseCode, byte resultCount, ushort chunkSize = 1024)
        {
            SearchFloppyResponse searchFloppyResponse = new SearchFloppyResponse();
            searchFloppyResponse.ResponseCode = responseCode;

            if (resultCount > 0)
            {
                searchFloppyResponse.SyncByte = (byte)'+';
                searchFloppyResponse.ResultCount = resultCount;

                // filled in later
                // send binary length in 24 bits
                //int lengthMinusMemoryPtr = payload.Length - 2;
                //searchFloppyResponse.ByteCountLo = (byte)(lengthMinusMemoryPtr & 0xFF); // LSB
                //searchFloppyResponse.ByteCountMid = (byte)((lengthMinusMemoryPtr >> 8) & 0xFF);
                //searchFloppyResponse.ByteCountHi = (byte)((lengthMinusMemoryPtr >> 16) & 0xFF); // MSB

                byte loChunkLength = (byte)chunkSize;
                byte hiChunkLength = (byte)(chunkSize >> 8);
                searchFloppyResponse.ChunkSizeLo = loChunkLength;
                searchFloppyResponse.ChunkSizeHi = hiChunkLength;

                // int memoryLocation = (payload[1] << 8) + payload[0];

                byte loDestPtr = (byte)destPtr;
                byte hiDestPtr = (byte)(destPtr >> 8);

                searchFloppyResponse.DestPtrLo = loDestPtr;
                searchFloppyResponse.DestPtrHi = hiDestPtr;
            }

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
