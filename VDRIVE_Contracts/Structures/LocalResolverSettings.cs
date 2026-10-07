namespace VDRIVE_Contracts.Structures
{
    public class LocalResolverSettings
    {
        public List<string> SearchPaths { get; set; }
        public List<string> MediaExtensionsAllowed { get; set; }
        public List<string> IgnoredSearchKeywords { get; set; }
        public bool EnableRecursiveSearch { get; set; }
        // web player: "Game - Disk1.d64" is sent together with "Game - Disk2.d64" (same folder) as one ZIP for the Disk Box
        public bool GroupMultiDiskImages { get; set; }
        // words that mark a disk in a file name, followed by a number or letter: "Disk1", "Side A" (default: disk, disc, side)
        public List<string> DiskMarkers { get; set; }
        // un-numbered disks that belong to a set and go first: "Title - GAME.d64", "Title - BOOT.d64"
        // (default: game, boot, program, main, intro, start, loader)
        public List<string> CompanionDiskNames { get; set; }
    }
}
