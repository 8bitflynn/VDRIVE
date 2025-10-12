using Microsoft.Extensions.Configuration;
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
            IFloppyResolver floppyResolver = new CommodoreSoftwareFloppyResolver(configuration, logger);// new LocalFloppyResolver(configuration, logger);
            ILoad loader = new ViceLoad(configuration, logger);
            ISave saver = new ViceSave(configuration, logger);

            // hack until the search request is coming from C64
            // search for floppy images in configured search paths
            SearchFloppiesRequest searchFloppiesRequest = new SearchFloppiesRequest();
            searchFloppiesRequest.SearchTerm = "assembler".ToCharArray();
            searchFloppiesRequest.SearchTermLength = (byte)searchFloppiesRequest.SearchTerm.Length;
            searchFloppiesRequest.MediaType = "d64";
            searchFloppiesRequest.MediaTypeLength = (byte)searchFloppiesRequest.MediaType.Length;

            SearchFloppyResponse searchFloppyResponse = floppyResolver.SearchFloppys(searchFloppiesRequest);
           
            // just pick a random disk from the search results
            int randomDisk = Random.Shared.Next(0, searchFloppyResponse.SearchResults.Count() - 1);

            // HACK until I get the floppy resolver implemented from C64
            FloppyInfo floppyInfo = searchFloppyResponse.SearchResults.ElementAt(randomDisk);
            floppyResolver.InsertFloppy(floppyInfo);

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
