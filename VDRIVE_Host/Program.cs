using VDRIVE;
using VDRIVE.Configuration;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE_Host
{
    public class Program
    {
        /// <summary>
        /// 
        /// CURRENT TESTING
        /// SYS 49152 - enable
        /// SYS 49158 - search floppies (will prompt for search term in C64)
        /// SYS 49161 - insert floppy (will prompt for ID in C64)
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            Program program = new Program();

            VDRIVE_Contracts.Interfaces.IConfigurationBuilder configBuilder = new ConfigurationBuilder();
            VDRIVE_Contracts.Interfaces.IConfiguration configuration = configBuilder.BuildConfiguration();

            ILog logger = new VDRIVE.Util.ConsoleLogger();

            // firmware is setup as client mode by default so run this in server mode
            // should allow multiple C64 connections to same disk image but
            // might need to put some locks in place for anything shared access         
            Server server = new Server(configuration, logger);
            server.Start();

            // client mode is nice if you cannot change firewall settings as ESP8266 does not have a firewall!
            //string ipAddress = "xxx.xxx.xxx.xxx";
            //int port = 80;

            //Client client = new Client(ipAddress, port, configuration, logger);
            //client.Start();
        }
    }
}
