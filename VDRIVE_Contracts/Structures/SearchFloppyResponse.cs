namespace VDRIVE_Contracts.Structures
{
    public struct SearchFloppyResponse
    {
        public SearchFloppyResponse() { }

        public IList<FloppyInfo> SearchResults = new List<FloppyInfo>();        
    }
}
