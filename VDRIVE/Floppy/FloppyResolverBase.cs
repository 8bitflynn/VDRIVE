using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Floppy
{
    public abstract class FloppyResolverBase : IFloppyResolver
    {
        protected IConfiguration Configuration;
        protected IVDriveLoggger Logger;
        protected FloppyInfo InsertedFloppyInfo; // info returned to C64
        protected List<FloppyInfo> FloppyInfos = new List<FloppyInfo>(); // C64 friendly floppy info
        protected FloppyPointer InsertedFloppyPointer; // join to FloppyInfo.Id for long path
        protected List<FloppyPointer> FloppyPointers = new List<FloppyPointer>(); // modern side long path joined to FloppyInfo.Id   

        public virtual FloppyInfo InsertFloppy(FloppyIdentifier floppyIdentifier) // called from a client 
        {
            this.InsertedFloppyInfo = this.FloppyInfos.FirstOrDefault(fi => fi.IdLo == floppyIdentifier.IdLo && fi.IdHi == floppyIdentifier.IdHi);
            this.InsertedFloppyPointer = this.FloppyPointers.FirstOrDefault(fp => fp.Id == (floppyIdentifier.IdLo | (floppyIdentifier.IdHi << 8)));

            if (!this.InsertedFloppyInfo.Equals(default(FloppyInfo)))
            {
                string floppyName = new string(this.InsertedFloppyInfo.ImageName);
                this.Logger.LogMessage($"Inserting floppy: {floppyName} ID={(floppyIdentifier.IdLo | (floppyIdentifier.IdHi << 8))}");
            }
            return this.InsertedFloppyInfo; 
        }

        public FloppyInfo InsertFloppy(FloppyInfo floppyInfo) // easier locally
        {
            FloppyIdentifier floppyIdentifier = new FloppyIdentifier { IdLo = floppyInfo.IdLo, IdHi = floppyInfo.IdHi };
            return ((IFloppyResolver)this).InsertFloppy(floppyIdentifier);
        }

        public FloppyInfo EjectFloppy()
        {
            this.FloppyInfos.Clear();
            this.FloppyPointers.Clear();

            this.Logger.LogMessage("Ejecting floppy: " + new string (this.InsertedFloppyInfo.ImageName).TrimEnd());
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

        public FloppyPointer SetInsertedFloppyPointer(FloppyPointer floppyPointer)
        {
            this.InsertedFloppyPointer = floppyPointer;
            return this.InsertedFloppyPointer;
        }

        protected void ClearSearchResults()
        {
            this.FloppyInfos.Clear();
            this.FloppyPointers.Clear();
        }

        protected void ExtractSearchInfo(SearchFloppiesRequest searchFloppiesRequest, out string searchTerm, out string mediaTypeCSV, out string[] mediaTypes)
        {
            searchTerm = new string(searchFloppiesRequest.SearchTerm.TakeWhile(c => c != '\0').ToArray());
            if (searchFloppiesRequest.MediaTypeLength == 0)
            {
                mediaTypeCSV = this.Configuration.MediaExtensionAllowed;
            }
            else
            {
                mediaTypeCSV = new string(searchFloppiesRequest.MediaType).TrimEnd();
            }
            
            if (!string.IsNullOrWhiteSpace(mediaTypeCSV))
            {
                mediaTypes = mediaTypeCSV.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
            else
            {
                mediaTypes = this.Configuration.MediaExtensionAllowed.Split(',');
            }
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
