using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Floppy
{
    public abstract class FloppyResolverBase : IFloppyResolver
    {
        protected IConfiguration Configuration;
        protected ILogger Logger;
        protected ISessionProvider SessionProvider;
        protected FloppyInfo InsertedFloppyInfo; // info returned to C64
        protected List<FloppyInfo> FloppyInfos = new List<FloppyInfo>(); // C64 friendly floppy info
        protected FloppyPointer InsertedFloppyPointer; // join to FloppyInfo.Id for long path
        protected List<FloppyPointer> FloppyPointers = new List<FloppyPointer>(); // modern side long path joined to FloppyInfo.Id   

        public virtual FloppyInfo InsertFloppy(FloppyIdentifier floppyIdentifier) // called from a client 
        {
            this.InsertedFloppyInfo = this.FloppyInfos.FirstOrDefault(fi => fi.IdLo == floppyIdentifier.IdLo && fi.IdHi == floppyIdentifier.IdHi);
            this.InsertedFloppyPointer = this.FloppyPointers.FirstOrDefault(fp => fp.Id == (floppyIdentifier.IdLo | (floppyIdentifier.IdHi << 8)));

            if (!this.InsertedFloppyPointer.Equals(default(FloppyPointer)) && !this.InsertedFloppyPointer.Equals(default))
            {
                string floppyName = new string(this.InsertedFloppyInfo.ImageName.TakeWhile(c => c != '\0').ToArray());
                string fullFloppyPath = this.InsertedFloppyPointer.ImagePath;
                this.Logger.LogMessage($"Inserting floppy: {floppyName} from {fullFloppyPath} ID={(floppyIdentifier.IdLo | (floppyIdentifier.IdHi << 8))}");
            }
            return this.InsertedFloppyInfo;
        }

        public FloppyInfo InsertFloppy(FloppyInfo floppyInfo) // easier locally
        {
            if (floppyInfo.ImageNameLength == 0)
            {
                FloppyIdentifier floppyIdentifier = new FloppyIdentifier { IdLo = floppyInfo.IdLo, IdHi = floppyInfo.IdHi };
                return ((IFloppyResolver)this).InsertFloppy(floppyIdentifier);
            }
            else
            {
                FloppyIdentifier floppyIdentifier = FindFloppyIdentifierByName(new string(floppyInfo.ImageName).TrimEnd('\0'));
                if (floppyIdentifier.Equals(default(FloppyIdentifier)))
                {
                    this.Logger.LogMessage("Floppy not found: " + new string(floppyInfo.ImageName.TakeWhile(c => c != '\0').ToArray()));
                    return default(FloppyInfo);
                }
                
                return ((IFloppyResolver)this).InsertFloppy(floppyIdentifier);
            }
        }

        public FloppyIdentifier FindFloppyIdentifierByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return default;
            }

            char[] nameChars = name.PadRight(64, '\0').ToArray();
            FloppyInfo foundFloppyInfo = this.FloppyInfos.FirstOrDefault(fi => fi.ImageName.SequenceEqual(nameChars));
            
            if (foundFloppyInfo.Equals(default(FloppyInfo)))
            {
                this.Logger.LogMessage($"Floppy identifier not found for name: {name}");
                return default;
            }

            return new FloppyIdentifier { IdLo = foundFloppyInfo.IdLo, IdHi = foundFloppyInfo.IdHi };
        }

        public FloppyInfo FindFloppyInfoByIdentifier(FloppyIdentifier floppyIdentifier)
        {
            FloppyInfo foundFloppyInfo = this.FloppyInfos.FirstOrDefault(fi => fi.IdLo == floppyIdentifier.IdLo && fi.IdHi == floppyIdentifier.IdHi);
            
            if (foundFloppyInfo.Equals(default(FloppyInfo)))
            {
                this.Logger.LogMessage($"Floppy info not found for identifier: {(floppyIdentifier.IdLo | (floppyIdentifier.IdHi << 8))}");
            }

            return foundFloppyInfo;
        }

        public FloppyInfo EjectFloppy()
        {
            this.FloppyInfos.Clear();
            this.FloppyPointers.Clear();

            string floppyName = new string(this.InsertedFloppyInfo.ImageName.TakeWhile(c => c != '\0').ToArray());
            this.Logger.LogMessage("Ejecting floppy: " + floppyName);
            this.InsertedFloppyInfo = default(FloppyInfo);
            this.InsertedFloppyPointer = default(FloppyPointer);
            return this.InsertedFloppyInfo;
        }

        public FloppyInfo GetInsertedFloppyInfo()
        {
            return this.InsertedFloppyInfo;
        }

        public FloppyPointer GetInsertedFloppyPointer()
        {
            return this.InsertedFloppyPointer;
        }

        public abstract SearchFloppyResponse SearchFloppys(SearchFloppiesRequest searchFloppiesRequest, out FloppyInfo[] foundFloppyInfos);    

        protected void ClearSearchResults()
        {
            this.FloppyInfos.Clear();
            this.FloppyPointers.Clear();
        }      

        protected string ResolvePrimaryDisk(IEnumerable<string> extractFullFilePaths, IEnumerable<string> mediaExtensionsAllowed)
        {
            if (InsertedFloppyPointer.Equals(default))
                return null;

            var allowedExtensions = mediaExtensionsAllowed
                .Select(ext => ext.ToLowerInvariant())
                .ToHashSet();

            return extractFullFilePaths
                .Where(path => allowedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
                .OrderByDescending(path => ScoreFilename(Path.GetFileName(path)))
                .FirstOrDefault();
        }

        protected int ScoreFilename(string name)
        {
            name = name.ToLowerInvariant();

            if (name.Contains("disk1") || name.Contains("side1") ||
                name.Contains("disk 1") || name.Contains("side 1") ||
                name.Contains("diska") || name.Contains("sidea") ||
                name.Contains("disk a") || name.Contains("side a") ||
                name.Contains("main") || name.Contains("boot"))
                return 100;

            if (name.Contains("disk2") || name.Contains("side2") ||
                name.Contains("disk 2") || name.Contains("side 2") ||
                name.Contains("diskb") || name.Contains("sideb") ||
                name.Contains("disk b") || name.Contains("side b") ||
                name.Contains("extra") || name.Contains("docs"))
                return 50;

            if (name.Contains("readme") || name.Contains("manual"))
                return 10;

            return 25; // neutral fallback
        }

        protected virtual SearchFloppyResponse BuildSearchFloppyResponse(ushort destPtr, byte responseCode, byte resultCount)
        {
            SearchFloppyResponse searchFloppyResponse = new SearchFloppyResponse();
            searchFloppyResponse.ResponseCode = responseCode;

            if (resultCount > 0)
            {
                searchFloppyResponse.SyncByte = (byte)'+';
                searchFloppyResponse.ResultCount = resultCount;

                byte loChunkLength = (byte)this.Configuration.ChunkSize;
                byte hiChunkLength = (byte)(this.Configuration.ChunkSize >> 8);
                searchFloppyResponse.ChunkSizeLo = loChunkLength;
                searchFloppyResponse.ChunkSizeHi = hiChunkLength;

                byte loDestPtr = (byte)destPtr;
                byte hiDestPtr = (byte)(destPtr >> 8);

                searchFloppyResponse.DestPtrLo = loDestPtr;
                searchFloppyResponse.DestPtrHi = hiDestPtr;
            }

            return searchFloppyResponse;
        }
    }
}
