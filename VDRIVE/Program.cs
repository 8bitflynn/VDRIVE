using Microsoft.Extensions.Configuration;
using System.Runtime.CompilerServices;
using VDRIVE.Disk.Vice;
using VDRIVE.Floppy;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE
{
    public class Program
    {
        static void Main(string[] args)
        {
            Program program = new Program();

            VDRIVE_Contracts.Interfaces.IConfiguration configuration = program.BuildConfiguration();

            // injected dependencies
            ILog logger = new Util.ConsoleLogger();
            //IFloppyResolver floppyResolver = new LocalFloppyResolver(configuration, logger); // search local paths
            IFloppyResolver floppyResolver = new CommodoreSoftwareFloppyResolver(configuration, logger); // search commodoresoftware.com
            ILoad loader = new ViceLoad(configuration, logger);
            ISave saver = new ViceSave(configuration, logger);

            // HACK until the search request is coming from C64
            // search for floppy images in configured search paths
            int selectedDisk = 0;
            SearchFloppyResponse? searchFloppyResponse = null;
            while (selectedDisk <= 0)
            {
                logger.LogMessage("Select a disk image to insert (0 to search again):");
                searchFloppyResponse = Search(floppyResolver, logger);
                if (searchFloppyResponse == null)
                {
                    logger.LogMessage("No search results");
                    continue;
                }
                string selectedDiskString = Console.ReadLine();
                int.TryParse(selectedDiskString, out selectedDisk);
            }

            // just pick a random disk from the search results
            //int randomDisk = Random.Shared.Next(0, searchFloppyResponse.SearchResults.Count() - 1);           

            // HACK until I get the floppy resolver implemented from C64
            FloppyInfo floppyInfo = searchFloppyResponse.Value.SearchResults.ElementAt(selectedDisk-1); // pick top result
            FloppyInfo? insertedFloppy = floppyResolver.InsertFloppy(floppyInfo);         

            if (insertedFloppy == null)
                throw new Exception("Failed to insert floppy!"); // send error to C64 

            // firmware is setup as client mode by default so run this in server mode
            // should allow multiple C64 connections to same disk image but
            // might need to put some locks in place for anything shared access         
            Server server = new Server(configuration, floppyResolver, loader, saver, logger);
            server.Start();

            // client mode is nice if you cannot change firewall settings as ESP8266 does not have one!
            //string ipAddress = "xxx.xxx.xxx.xxx";
            //int port = 80;

            //Client client = new Client(ipAddress, port, configuration, floppyResolver, loader, saver, logger);
            //client.Start();
        }

        private static SearchFloppyResponse? Search(IFloppyResolver floppyResolver, ILog logger)
        {
            logger.LogMessage("https://commodore.software/");
            logger.LogMessage("Enter search term");
            string searchTerm = Console.ReadLine();

            SearchFloppiesRequest searchFloppiesRequest = new SearchFloppiesRequest();
            searchFloppiesRequest.SearchTerm = searchTerm.ToCharArray();
            searchFloppiesRequest.SearchTermLength = (byte)searchFloppiesRequest.SearchTerm.Length;
            searchFloppiesRequest.MediaType = "d64,d81,d71,g64,t64,prg,p00,zip";
            searchFloppiesRequest.MediaTypeLength = (byte)searchFloppiesRequest.MediaType.Length;

            SearchFloppyResponse searchFloppyResponse = floppyResolver.SearchFloppys(searchFloppiesRequest);

            if (searchFloppyResponse.SearchResults == null || searchFloppyResponse.SearchResults.Count() == 0)
            {
                logger.LogMessage("No floppy images found!");
                return null;
            }

            foreach (FloppyInfo foundFloppyInfo in searchFloppyResponse.SearchResults)
            {
                logger.LogMessage($"[{foundFloppyInfo.IdLo | foundFloppyInfo.IdHi << 8}] {new string(foundFloppyInfo.ImageName)}"); // - {new string(foundFloppyInfo.Description)}");
            }

            return searchFloppyResponse;
        }
     
        VDRIVE_Contracts.Interfaces.IConfiguration BuildConfiguration()
        {
            VDRIVE_Contracts.Interfaces.IConfiguration configuration = new Configuration();

            var configurationBuilder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            configuration.SearchPaths = configurationBuilder.GetSection("AppSettings:SearchPaths").Get<List<string>>();
            configuration.C1541Path = configurationBuilder.GetSection("AppSettings:C1541Path").Value;
            configuration.TempPath = configurationBuilder.GetSection("AppSettings:TempPath").Value;

            if (string.IsNullOrEmpty(configuration.TempPath))
            {
                configuration.TempPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\C64Temp\";
            }

            return configuration;
        }     
    }
}
