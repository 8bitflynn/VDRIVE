namespace VDRIVE_Contracts.Structures
{
    public class LocalResolverSettings
    {
        public List<string> SearchPaths { get; set; }
        public List<string> MediaExtensionsAllowed { get; set; }
        public List<string> IgnoredSearchKeywords { get; set; }
        public bool EnableRecursiveSearch { get; set; }
    }
}
