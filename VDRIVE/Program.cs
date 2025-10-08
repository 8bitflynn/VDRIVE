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
            IFloppyResolver floppyResolver = new LocalFloppyResolver(configuration);
            ILoad loader = new ViceLoad(configuration.C1541Path);
            ISave saver = new ViceSave(configuration.C1541Path);
            ILog logger = new Util.ConsoleLogger();

            // HACK until I get the floppy resolver implemented from C64           
            floppyResolver.InsertFloppyByPath(configuration.SearchPaths.First() + "data4.d64");

            // firmware is setup as client by default so run this in server mode
            // should allow multiple C64 connections to same disk image but
            // might need to put some locks in place and I have yet to test
            // with multiple C64s (should be fun!)
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

            return configuration;
        }
    }
}
