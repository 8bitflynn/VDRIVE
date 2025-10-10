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
            IFloppyResolver floppyResolver = new LocalFloppyResolver(configuration);
            ILoad loader = new ViceLoad(configuration, logger);
            ISave saver = new ViceSave(configuration, logger);           
           
            // hack until the search request is coming from C64
            // search for floppy images in configured search paths
            SearchFloppiesRequest searchFloppiesRequest = new SearchFloppiesRequest()
            {
                Description = "data",
                MediaType = "d64, g64, d71, d81"
            };

            SearchFloppyResponse searchFloppyResponse = floppyResolver.SearchFloppys(searchFloppiesRequest);

            // HACK until I get the floppy resolver implemented from C64           
            floppyResolver.InsertFloppy(searchFloppyResponse.SearchResults.First());

            // firmware is setup as client by default so run this in server mode
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
                configuration.TempPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            return configuration;
        }     
    }
}
