using System.Runtime.CompilerServices;
using VDRIVE;
using VDRIVE.Configuration;
using VDRIVE.Drive;
using VDRIVE.Floppy;
using VDRIVE.Util;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE_Host
{
    public class Program
    {
        /// <summary>
        /// 
        /// CURRENT TESTING on C64
        /// SYS 49152 - enable
        /// SYS 49158 - search floppies (will prompt for search term in C64, enter the number to mount)
        /// SYS 49161 - insert floppy (will prompt for ID in C64) - already done i n previous step but user can change floppy here
        /// </summary>
        /// <param name="args"></param>
        static void Main(string[] args)
        {
            ILogger logger = new VDRIVE.Util.ConsoleLogger();

            VDRIVE_Contracts.Interfaces.IConfigurationBuilder configBuilder = new ConfigurationBuilder(logger);
            VDRIVE_Contracts.Interfaces.IConfiguration configuration = configBuilder.BuildConfiguration();

            if (!configBuilder.IsValidConfiguration(configuration))
            {
                logger.LogMessage("Invalid configuration, exiting");
                return;
            }

            configBuilder.DumpConfiguration(configuration);

            switch (configuration.ServerOrClientMode)
            {
                case "Server":                    
                    
                    IVDriveServer server = VDriveFactory.CreateVDriveServer(configuration.ServerType, configuration, logger);
                    server.Start();
                    break;

                case "Client":
                    // client mode is nice if you cannot change firewall settings as ESP8266 does not have a firewall!                 
                    IVDriveClient client = VDriveFactory.CreateVDriveClient(configuration.ServerType, configuration, logger);
                    client.Start();
                    break;                
            }            
        }      
    }
}
