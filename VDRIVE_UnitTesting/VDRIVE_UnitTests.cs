using VDRIVE.Configuration;
using VDRIVE.Drive;
using VDRIVE.Drive.Impl;
using VDRIVE.Floppy;
using VDRIVE.Util;
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
            IProcessRunner processRunner = new LockingProcessRunner(configuration, logger);

            if (!configBuilder.IsValidConfiguration(configuration))
            {
                return;
            }
            
            SearchFloppiesRequest searchFloppyRequest = new SearchFloppiesRequest(); // input from C64
            searchFloppyRequest.SearchTerm = "ozzy".ToArray();
            searchFloppyRequest.SearchTermLength = (byte)searchFloppyRequest.SearchTerm.Length;

            IFloppyResolver floppyResolver = FloppyResolverFactory.CreateFloppyResolver(configuration.FloppyResolver, configuration, logger, processRunner);
            SearchFloppyResponse searchFloppyResponse = floppyResolver.SearchFloppys(searchFloppyRequest, out FloppyInfo[] floppyInfo); // output to C64


            FloppyIdentifier floppyIdentifier = new FloppyIdentifier(); // input from C64 (user selected ID)
            floppyIdentifier.IdLo = floppyInfo[0].IdLo;
            floppyIdentifier.IdHi = floppyInfo[0].IdHi;

            FloppyInfo insertedFloppy = floppyResolver.InsertFloppy(floppyIdentifier); // can insert floppy with full FloppyInfo too

            LoadRequest loadRequest = new LoadRequest(); // input from C64
            loadRequest.Operation = 1;
            loadRequest.FileName = "8bitintro".ToArray();
            loadRequest.FileNameLength = (byte)loadRequest.FileName.Length;

            IStorageAdapter storageAdapter = StorageAdapterFactory.CreateStorageAdapter(configuration.StorageAdapter, processRunner, configuration, logger);
            LoadResponse loadResponse = storageAdapter.Load(loadRequest, floppyResolver, out byte[] payload); // output to C64
        }

        [TestMethod]
        public void TestSearchLoadSaveConcurrent()
        {
            ILogger logger = new VDRIVE.Util.ConsoleLogger();

            VDRIVE_Contracts.Interfaces.IConfigurationBuilder configBuilder = new ConfigurationBuilder(logger);
            VDRIVE_Contracts.Interfaces.IConfiguration configuration = configBuilder.BuildConfiguration();
            IProcessRunner processRunner = new LockingProcessRunner(configuration, logger);

            if (!configBuilder.IsValidConfiguration(configuration))
            {
                return;
            }

            configBuilder.DumpConfiguration(configuration);

            SearchFloppiesRequest searchFloppyRequest = new SearchFloppiesRequest(); // input from C64
            searchFloppyRequest.SearchTerm = "data4".ToArray();
            searchFloppyRequest.SearchTermLength = (byte)searchFloppyRequest.SearchTerm.Length;

            IFloppyResolver floppyResolver = FloppyResolverFactory.CreateFloppyResolver(configuration.FloppyResolver, configuration, logger, processRunner);
            SearchFloppyResponse searchFloppyResponse = floppyResolver.SearchFloppys(searchFloppyRequest, out FloppyInfo[] floppyInfo); // output to C64

            FloppyIdentifier floppyIdentifier = new FloppyIdentifier(); // input from C64 (user selected ID)
            floppyIdentifier.IdLo = floppyInfo[0].IdLo;
            floppyIdentifier.IdHi = floppyInfo[0].IdHi;

            FloppyInfo insertedFloppy = floppyResolver.InsertFloppy(floppyIdentifier); // sent from C64 - 2 byte floppy ID

            // read/write to same disk/file in different threads
            // to test concurrency
            int testIterations = 25;
            List<Task> tasks = new List<Task>();
            Task task1 = Task.Run(() =>
            {
                int count = 0;
                while (count < testIterations)
                {
                    LoadRequest loadRequest = new LoadRequest(); // input from C64
                    loadRequest.Operation = 1;
                    loadRequest.FileName = "8bitintro".ToArray();
                    loadRequest.FileNameLength = (byte)loadRequest.FileName.Length;

                    IProcessRunner processRunner = new LockingProcessRunner(configuration, logger);
                    IStorageAdapter storageAdapter = StorageAdapterFactory.CreateStorageAdapter(configuration.StorageAdapter, processRunner, configuration, logger);
                    LoadResponse loadResponse = storageAdapter.Load(loadRequest, floppyResolver, out byte[] payload); // output to C64

                    count++;
                }
            });

            tasks.Add(task1);

            Task task2 = Task.Run(() =>
            {
                int count = 0;
                while (count < testIterations)
                {
                  
                    SaveRequest saveRequest = new SaveRequest(); // input from C64
                    saveRequest.Operation = 1;
                    saveRequest.FileName = "8bitintro".ToArray();
                    saveRequest.FileNameLength = (byte)saveRequest.FileName.Length;

                    byte[] payload = File.ReadAllBytes(@"c:\temp\8bitintro.prg");

                    ushort destAddr = (ushort)(payload[0] | payload[1] << 8);
                    saveRequest.TargetAddressLo = payload[0];
                    saveRequest.TargetAddressHi = payload[1];

                    payload = payload.Skip(2).ToArray(); // skip destination pointer                     

                    IProcessRunner processRunner = new LockingProcessRunner(configuration, logger);
                    IStorageAdapter storageAdapter = StorageAdapterFactory.CreateStorageAdapter(configuration.StorageAdapter, processRunner, configuration, logger);
                    SaveResponse saveResponse = storageAdapter.Save(saveRequest, floppyResolver, payload); // inptu from C64

                    count++;
                }
            });

            tasks.Add(task2);

            Task.WaitAll(tasks.ToArray());
        }
    }
}