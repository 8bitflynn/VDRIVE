using VDRIVE.Configuration;
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
        public void TestSearchLoad()
        {
            ILogger logger = new VDRIVE.Util.ConsoleLogger();

            VDRIVE_Contracts.Interfaces.IConfigurationBuilder configBuilder = new ConfigurationBuilder(logger);
            VDRIVE_Contracts.Interfaces.IConfiguration configuration = configBuilder.BuildConfiguration();

            IFloppyResolver floppyResolver = FloppyResolverFactory.CreateFloppyResolver("Local", configuration, logger);

            SearchFloppiesRequest searchFloppyRequest = new SearchFloppiesRequest();
            searchFloppyRequest.SearchTerm = "data6".ToArray();
            searchFloppyRequest.SearchTermLength = (byte)searchFloppyRequest.SearchTerm.Length;

            SearchFloppyResponse searchFloppyResponse =  floppyResolver.SearchFloppys(searchFloppyRequest, out FloppyInfo[]  floppyInfo);

            floppyResolver.InsertFloppy(floppyInfo[0]);

            LoadRequest loadRequest = new LoadRequest();
            loadRequest.Operation = 1;
            loadRequest.FileName = "$".ToArray();     
            loadRequest.FileNameLength = (byte)loadRequest.FileName.Length; 

            IStorageAdapter storageAdapter = new DirMasterStorageAdapter(configuration, logger);
            storageAdapter.Load(loadRequest, floppyResolver, out byte[] payload);           
        }
    }
}