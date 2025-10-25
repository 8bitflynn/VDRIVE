using VDRIVE.Floppy.Impl;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE.Floppy
{
    public class FloppyResolverFactory
    {
        public static IFloppyResolver CreateFloppyResolver(string resolverType, IConfiguration configuration, ILogger logger)
        {
            switch (resolverType)
            {
                case "Local":
                    return new LocalFloppyResolver(configuration, logger);
                case "CommodoreSoftware":
                    return new CommodoreSoftwareFloppyResolver(configuration, logger);
                case "C64":
                    return new C64FloppyResolver(configuration, logger);
                default:
                    throw new ArgumentException($"Unknown floppy resolver type: {resolverType}");
            }
        }
    }
}
