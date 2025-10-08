using VDRIVE_Contracts.Interfaces;

namespace VDRIVE.Floppy
{
    public class LocalFloppyResolver : IFloppyResolver
    {
        public LocalFloppyResolver(IConfiguration configuration)
        {
            this.Configuration = configuration;
        }
        private IConfiguration Configuration;
        private string ImagePath;

        string IFloppyResolver.InsertFloppyByPath(string path)
        {
            this.ImagePath = path;
            return this.ImagePath; // should work for now
        }
        void IFloppyResolver.EjectFloppy()
        {
            this.ImagePath = null;
        }
        string IFloppyResolver.GetInsertedFloppyPath()
        {
            return this.ImagePath;
        }

        IList<string> IFloppyResolver.SearchFloppys(string searchPattern)
        {
            // TODO: this should return a list of images matching the search
            // along with a number for each to be used for selection
            return new List<string>();
        }

        string IFloppyResolver.InsertFloppyById(string id)
        {
            // TODO: search will return images that match search
            return this.ImagePath; // should work for now
        }
    }
}
