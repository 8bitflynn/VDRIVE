namespace VDRIVE_Contracts.Structures
{
    public class FloppyResolverSettings
    {
        public LocalResolverSettings Local { get; set; }
        public CommodoreSoftwareSettings CommodoreSoftware { get; set; }
        public HcsvPsid HvscPsid { get; set; }
    }
}
