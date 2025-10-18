using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Floppy
{
    public abstract class FloppyResolverBase : IFloppyResolver
    {
        protected IConfiguration Configuration;
        protected ILog Logger;
        protected FloppyInfo? InsertedFloppyInfo; // info returned to C64
        protected List<FloppyInfo> FloppyInfos = new List<FloppyInfo>();
        protected FloppyPointer? InsertedFloppyPointer; // join to FloppyInfo.Id for long path
        protected List<FloppyPointer> FloppyPointers = new List<FloppyPointer>();

        protected List<string> DefaultMediaExtensionsAllowed = new List<string> { ".d64", ".g64", ".d81", ".d71", ".d80", ".d82", ".prg" };

        // set when searching disk
        protected List<string> MediaExtensionsAllowed = new List<string>();

        public virtual FloppyInfo? InsertFloppy(FloppyIdentifier floppyIdentifier) // called from a client 
        {
            this.InsertedFloppyInfo = this.FloppyInfos.FirstOrDefault(fi => fi.IdLo == floppyIdentifier.IdLo && fi.IdHi == floppyIdentifier.IdHi);
            this.InsertedFloppyPointer = this.FloppyPointers.FirstOrDefault(fp => fp.Id == (floppyIdentifier.IdLo | (floppyIdentifier.IdHi << 8)));

            if (this.InsertedFloppyInfo.HasValue)
            {
                string floppyName = new string(this.InsertedFloppyInfo.Value.ImageName);
                this.Logger.LogMessage($"Inserting floppy: {floppyName} ID={(floppyIdentifier.IdLo | (floppyIdentifier.IdHi << 8))}");
            }
            return this.InsertedFloppyInfo.Value; 
        }

        public FloppyInfo? InsertFloppy(FloppyInfo floppyInfo) // easier locally
        {
            FloppyIdentifier floppyIdentifier = new FloppyIdentifier { IdLo = floppyInfo.IdLo, IdHi = floppyInfo.IdHi };
            return ((IFloppyResolver)this).InsertFloppy(floppyIdentifier);
        }

        public FloppyInfo? EjectFloppy()
        {
            this.Logger.LogMessage(Logger is null ? "Ejecting floppy" : "Ejecting floppy: " + this.InsertedFloppyInfo?.ImageName);
            this.InsertedFloppyInfo = null;
            this.InsertedFloppyPointer = null;
            return this.InsertedFloppyInfo;
        }

        public FloppyInfo? GetInsertedFloppyInfo()
        {
            return this.InsertedFloppyInfo;
        }

        public FloppyPointer? GetInsertedFloppyPointer()
        {
            return this.InsertedFloppyPointer;
        }

        public abstract SearchFloppyResponse SearchFloppys(SearchFloppiesRequest searchFloppiesRequest, out FloppyInfo[] foundFloppyInfos);

        public FloppyPointer? SetInsertedFloppyPointer(FloppyPointer floppyPointer)
        {
            this.InsertedFloppyPointer = floppyPointer;
            return this.InsertedFloppyPointer;
        }
    }
}
