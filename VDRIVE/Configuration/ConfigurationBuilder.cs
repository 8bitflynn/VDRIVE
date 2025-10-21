using Microsoft.Extensions.Configuration;

namespace VDRIVE.Configuration
{
    public class ConfigurationBuilder : VDRIVE_Contracts.Interfaces.IConfigurationBuilder
    {
        public ConfigurationBuilder(VDRIVE_Contracts.Interfaces.IVDriveLoggger logger)
        {
            this.Logger = logger;
        }
        private readonly VDRIVE_Contracts.Interfaces.IVDriveLoggger Logger;

        public VDRIVE_Contracts.Interfaces.IConfiguration BuildConfiguration()
        {
            VDRIVE_Contracts.Interfaces.IConfiguration configuration = new VDRIVE_Contracts.Structures.Configuration();

            var configurationBuilder = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            configuration.FloppyResolver = configurationBuilder.GetSection("AppSettings:FloppyResolver").Value;
            configuration.SearchPaths = configurationBuilder.GetSection("AppSettings:SearchPaths").Get<List<string>>();
            configuration.MediaExtensionAllowed = configurationBuilder.GetSection("AppSettings:MediaExtensionAllowed").Value;
            configuration.C1541Path = configurationBuilder.GetSection("AppSettings:C1541Path").Value;
            configuration.TempPath = configurationBuilder.GetSection("AppSettings:TempPath").Value;
            if (string.IsNullOrEmpty(configuration.TempPath))
            {
                configuration.TempPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }
            configuration.TempFolder = configurationBuilder.GetSection("AppSettings:TempFolder").Value;
            configuration.ServerOrClientMode = configurationBuilder.GetSection("AppSettings:ServerOrClientMode").Value;
            configuration.ServerListenAddress = configurationBuilder.GetSection("AppSettings:ServerListenAddress").Value;
            configuration.ServerPort = int.TryParse(configurationBuilder.GetSection("AppSettings:ServerPort").Value, out int serverPort) ? serverPort : (int?)null;
            configuration.ClientAddress = configurationBuilder.GetSection("AppSettings:ClientAddress").Value;
            configuration.ClientPort = int.TryParse(configurationBuilder.GetSection("AppSettings:ClientPort").Value, out int clientTalkPort) ? clientTalkPort : (int?)null;
            configuration.SendTimeoutSeconds = int.TryParse(configurationBuilder.GetSection("AppSettings:SendTimeoutSeconds").Value, out int sendTimeout) ? sendTimeout : (int?)null;
            configuration.ReceiveTimeoutSeconds = int.TryParse(configurationBuilder.GetSection("AppSettings:ReceiveTimeoutSeconds").Value, out int receiveTimeout) ? receiveTimeout : (int?)null;
            if (ushort.TryParse(configurationBuilder.GetSection("AppSettings:ChunkSize").Value, out ushort chunkSize))
            {
                configuration.ChunkSize = chunkSize;
            }
            else
            {
                configuration.ChunkSize = 1024; // default to 1k
            }
            if (ushort.TryParse(configurationBuilder.GetSection("AppSettings:MaxSearchResults").Value, out ushort maxSearchResults))
            {
                configuration.MaxSearchResults = maxSearchResults;
            }
            else
            {
                configuration.MaxSearchResults = 18;
            }            

            return configuration;
        }

        public bool IsValidConfiguration(VDRIVE_Contracts.Interfaces.IConfiguration configuration)
        {
            if (configuration == null)
            {
                this.Logger.LogMessage("Configuration is null");
                return false;
            }

            if (string.IsNullOrWhiteSpace(configuration.FloppyResolver))
            {
                this.Logger.LogMessage("FloppyResolver is not set in configuration");
                return false;
            }

            if (string.IsNullOrWhiteSpace(configuration.ServerOrClientMode))
            {
                this.Logger.LogMessage("ServerOrClientMode is not set in configuration");
                return false;
            }
            else
            {
                if (configuration.ServerOrClientMode != "Server" && configuration.ServerOrClientMode != "Client")
                {
                    this.Logger.LogMessage("Invalid ServerOrClientMode in configuration");
                    return false;
                }
            }

            switch (configuration.ServerOrClientMode)
            {
                case "Server":                   
                    if (configuration.ServerPort == null || configuration.ServerPort < 1 || configuration.ServerPort > 65535)
                    {
                        this.Logger.LogMessage("ServerPort is not set correctly in configuration");
                        return false;
                    }
                    break;
                case "Client":
                    if (string.IsNullOrWhiteSpace(configuration.ClientAddress))
                    {
                        this.Logger.LogMessage("ClientAddress is not set in configuration");
                        return false;
                    }
                    if (configuration.ClientPort  == null || configuration.ClientPort < 1 || configuration.ClientPort > 65535)
                    {
                        this.Logger.LogMessage("ClientPort is not set correctly in configuration");
                        return false;
                    }
                    break;
                default:
                    this.Logger.LogMessage("ServerOrClientMode is not set correctly in configuration");
                    return false;
            }      

            return true;
        }

        public void DumpConfiguration(VDRIVE_Contracts.Interfaces.IConfiguration configuration)
        {
            if (configuration == null)
            {
                this.Logger.LogMessage("Configuration is null");
                return;
            }

            this.Logger.LogMessage("VDRIVE Configuration:");
            this.Logger.LogMessage($"  FloppyResolver: {configuration.FloppyResolver}");
            this.Logger.LogMessage($"  C1541Path: {configuration.C1541Path}");
            this.Logger.LogMessage($"  SearchPaths: {(string.Join(", ",configuration.SearchPaths))}");
            this.Logger.LogMessage($"  MediaExtensionAllowed: {configuration.MediaExtensionAllowed}");
            this.Logger.LogMessage($"  TempPath: {configuration.TempPath}");
            this.Logger.LogMessage($"  TempFolder: {configuration.TempFolder}");
            this.Logger.LogMessage($"  ServerOrClientMode: {configuration.ServerOrClientMode}");
            if (configuration.ServerOrClientMode == "Server")
            {
                this.Logger.LogMessage($"  ServerListenAddress: {configuration.ServerListenAddress}");
                this.Logger.LogMessage($"  ServerPort: {configuration.ServerPort}");
            }

            if (configuration.ServerOrClientMode == "Client")
            {
                this.Logger.LogMessage($"  ClientAddress: {configuration.ClientAddress}");
                this.Logger.LogMessage($"  ClientPort: {configuration.ClientPort}");
            }
            this.Logger.LogMessage($"  SendTimeoutSeconds: {configuration.SendTimeoutSeconds}");
            this.Logger.LogMessage($"  ReceiveTimeoutSeconds: {configuration.ReceiveTimeoutSeconds}");
            this.Logger.LogMessage($"  ChunkSize: {configuration.ChunkSize}");
        }        
    }
}
