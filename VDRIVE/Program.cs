using Microsoft.Extensions.Configuration;
using VDRIVE.Disk.Vice;
using VDRIVE.Floppy;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE
{
    public class Program
    {
        /// <summary>
        /// 
        /// CURRENT TESTING
        /// SYS 49152 - enable
        /// SYS 49158 - search floppyies (will prompt for search term in C64)
        /// SYS 49161 - insert floppy (will prompt for ID in C64)
        /// </summary>
        /// <param name="args"></param>
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

            // firmware is setup as client mode by default so run this in server mode
            // should allow multiple C64 connections to same disk image but
            // might need to put some locks in place for anything shared access         
            Server server = new Server(configuration, floppyResolver, loader, saver, logger);
            server.Start();

            // client mode is nice if you cannot change firewall settings as ESP8266 does not have a firewall!
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
            if (ushort.TryParse(configurationBuilder.GetSection("AppSettings:ChunkSize").Value, out ushort chunkSize))
            {
                configuration.ChunkSize = chunkSize;
            }
            else
            {
                configuration.ChunkSize = 1024; // default to 1k
            }


            if (string.IsNullOrEmpty(configuration.TempPath))
            {
                configuration.TempPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\C64Temp\";
            }

            return configuration;
        }
    }
}
