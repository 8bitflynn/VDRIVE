namespace VDRIVE_Contracts.Structures
{
    public struct SearchFloppiesRequest
    {
        public string Description; // search term
        public string MediaType; // D64/D71/D81        
        public int MaxResults; // max number of results to return         
    }
}
