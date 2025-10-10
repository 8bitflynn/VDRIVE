namespace VDRIVE_Contracts.Structures
{
    public struct FloppyInfo
    {
        public string ImagePath; // full path to image
        public string Id; // specific to IFloppyResolver implementation
        public string Description;
        public string MediaType; // D64/D71/D81
        public string WriteProtected;         
    }
}
