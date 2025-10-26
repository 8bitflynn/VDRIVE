using VDRIVE.Configuration;
using VDRIVE.Drive;
using VDRIVE.Drive.Impl;
using VDRIVE.Floppy;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE_UnitTesting
{
    [TestClass]
    public class VDRIVE_UnitTests
    {
        [TestMethod]
        public void TestSearch_Load()
        {
            ILogger logger = new VDRIVE.Util.ConsoleLogger();

            VDRIVE_Contracts.Interfaces.IConfigurationBuilder configBuilder = new ConfigurationBuilder(logger);
            VDRIVE_Contracts.Interfaces.IConfiguration configuration = configBuilder.BuildConfiguration();

            if (!configBuilder.IsValidConfiguration(configuration))
            {
                return;
            }
            
            SearchFloppiesRequest searchFloppyRequest = new SearchFloppiesRequest(); // input from C64
            searchFloppyRequest.SearchTerm = "data4".ToArray();
            searchFloppyRequest.SearchTermLength = (byte)searchFloppyRequest.SearchTerm.Length;

            IFloppyResolver floppyResolver = FloppyResolverFactory.CreateFloppyResolver(configuration.FloppyResolver, configuration, logger);
            SearchFloppyResponse searchFloppyResponse = floppyResolver.SearchFloppys(searchFloppyRequest, out FloppyInfo[] floppyInfo); // output to C64


            FloppyIdentifier floppyIdentifier = new FloppyIdentifier(); // input from C64 (user selected ID)
            floppyIdentifier.IdLo = floppyInfo[0].IdLo;
            floppyIdentifier.IdHi = floppyInfo[0].IdHi;

            FloppyInfo insertedFloppy = floppyResolver.InsertFloppy(floppyIdentifier); // can insert floppy with full FloppyInfo too

            LoadRequest loadRequest = new LoadRequest(); // input from C64
            loadRequest.Operation = 1;
            loadRequest.FileName = "8bitintro".ToArray();
            loadRequest.FileNameLength = (byte)loadRequest.FileName.Length;

            IStorageAdapter storageAdapter = StorageAdapterFactory.CreateStorageAdapter(configuration.StorageAdapter, configuration, logger);
            LoadResponse loadResponse = storageAdapter.Load(loadRequest, floppyResolver, out byte[] payload); // output to C64
        }       
    }
}