using Microsoft.Extensions.Configuration;

namespace VDRIVE.Configuration
{
    public class ConfigurationBuilder : VDRIVE_Contracts.Interfaces.IConfigurationBuilder
    {
        public ConfigurationBuilder(VDRIVE_Contracts.Interfaces.ILogger logger)
        {
            this.Logger = logger;
        }
        private readonly VDRIVE_Contracts.Interfaces.ILogger Logger;

        public VDRIVE_Contracts.Interfaces.IConfiguration BuildConfiguration()
        {
            IConfigurationRoot? configRoot = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();
            VDRIVE_Contracts.Interfaces.IConfiguration configuration = configRoot.GetSection("AppSettings").Get<VDRIVE_Contracts.Structures.Configuration>();

            if (string.IsNullOrEmpty(configuration.TempPath))
            {
                // use system default
                configuration.TempPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            configuration.ChunkSize = configuration.ChunkSize == 0 ? (ushort)1024 : configuration.ChunkSize;
            configuration.MaxSearchResults = configuration.MaxSearchResults == 0 ? (ushort)18 : configuration.MaxSearchResults;

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

            if (configuration.ServerOrClientMode != "Server" && configuration.ServerOrClientMode != "Client")
            {
                this.Logger.LogMessage("Invalid ServerOrClientMode in configuration");
                return false;
            }

            switch (configuration.ServerOrClientMode)
            {
                case "Server":
                    if (configuration.ServerPort is null || configuration.ServerPort < 1 || configuration.ServerPort > 65535)
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
                    if (configuration.ClientPort is null || configuration.ClientPort < 1 || configuration.ClientPort > 65535)
                    {
                        this.Logger.LogMessage("ClientPort is not set correctly in configuration");
                        return false;
                    }
                    break;
            }

            if (configuration.StorageAdapterSettings == null)
            {
                this.Logger.LogMessage("DriveSettings block is missing");
                return false;
            }

            switch (configuration.StorageAdapter)
            {
                case "DirMaster":
                    if (configuration.StorageAdapterSettings.DirMaster == null ||
                        string.IsNullOrWhiteSpace(configuration.StorageAdapterSettings.DirMaster.ExecutablePath) ||
                        string.IsNullOrWhiteSpace(configuration.StorageAdapterSettings.DirMaster.ScriptPath))
                    {
                        this.Logger.LogMessage("DirMaster settings are incomplete");
                        return false;
                    }
                    break;

                case "Vice":
                    if (configuration.StorageAdapterSettings.Vice == null ||
                        string.IsNullOrWhiteSpace(configuration.StorageAdapterSettings.Vice.ExecutablePath) ||
                        string.IsNullOrWhiteSpace(configuration.StorageAdapterSettings.Vice.Version))
                    {
                        this.Logger.LogMessage("Vice settings are incomplete");
                        return false;
                    }
                    break;              

                default:
                    this.Logger.LogMessage($"Unknown StorageAdapter type: {configuration.StorageAdapter}");
                    return false;
            }

            if (configuration.FloppyResolverSettings == null)
            {
                this.Logger.LogMessage("FloppyResolverSettings block is missing");
                return false;
            }

            switch (configuration.FloppyResolver)
            {
                case "Local":
                    if (configuration.FloppyResolverSettings.Local == null ||
                        configuration.FloppyResolverSettings.Local.SearchPaths == null ||
                        configuration.FloppyResolverSettings.Local.SearchPaths.Count == 0)
                    {
                        this.Logger.LogMessage("Local resolver settings are incomplete");
                        return false;
                    }
                    break;

                case "CommodoreSoftware":
                    if (configuration.FloppyResolverSettings.CommodoreSoftware == null ||
                        string.IsNullOrWhiteSpace(configuration.FloppyResolverSettings.CommodoreSoftware.BaseURL))
                    {
                        this.Logger.LogMessage("CommodoreSoftware resolver settings are incomplete");
                        return false;
                    }
                    break;

                default:
                    this.Logger.LogMessage($"Unknown FloppyResolver type: {configuration.FloppyResolver}");
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
            this.Logger.LogMessage($"  StorageAdapter: {configuration.StorageAdapter}");
            this.Logger.LogMessage($"  FloppyResolver: {configuration.FloppyResolver}");           
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
            this.Logger.LogMessage($"  MaxSearchResults: {configuration.MaxSearchResults}");

            // Storage adapter settings
            if (configuration.StorageAdapterSettings != null)
            {
                this.Logger.LogMessage("  StorageAdapterSettings:");
                if (configuration.StorageAdapterSettings.DirMaster != null && configuration.StorageAdapter == "DirMaster")
                {
                    this.Logger.LogMessage($"    DirMaster ExecutablePath: {configuration.StorageAdapterSettings.DirMaster.ExecutablePath}");
                    this.Logger.LogMessage($"    DirMaster ScriptPath: {configuration.StorageAdapterSettings.DirMaster.ScriptPath}");
                    this.Logger.LogMessage($"    DirMaster CBMDiskPath: {configuration.StorageAdapterSettings.DirMaster.CBMDiskPath}");
                }
                if (configuration.StorageAdapterSettings.Vice != null && configuration.StorageAdapter == "Vice")
                {
                    this.Logger.LogMessage($"    Vice ExecutablePath: {configuration.StorageAdapterSettings.Vice.ExecutablePath}");
                    this.Logger.LogMessage($"    Vice Version: {configuration.StorageAdapterSettings.Vice.Version}");
                    this.Logger.LogMessage($"    Vice ForceDeleteFirst: {configuration.StorageAdapterSettings.Vice.ForceDeleteFirst}"); 
                }               
            }

            // Floppy resolver settings
            if (configuration.FloppyResolverSettings != null)
            {
                this.Logger.LogMessage("  FloppyResolverSettings:");
                if (configuration.FloppyResolver == "Local" && configuration.FloppyResolverSettings.Local != null)
                {
                    this.Logger.LogMessage($"    Local SearchPaths: {string.Join(", ", configuration.FloppyResolverSettings.Local.SearchPaths)}");
                    this.Logger.LogMessage($"    Local MediaExtensionsAllowed: {string.Join(',', configuration.FloppyResolverSettings.Local.MediaExtensionsAllowed)}");
                    this.Logger.LogMessage($"    Local EnableRecursiveSearch: {configuration.FloppyResolverSettings.Local.EnableRecursiveSearch}");
                }
                if (configuration.FloppyResolver == "CommodoreSoftware" && configuration.FloppyResolverSettings.CommodoreSoftware != null)
                {
                    this.Logger.LogMessage($"    CommodoreSoftware BaseURL: {configuration.FloppyResolverSettings.CommodoreSoftware.BaseURL}");
                    this.Logger.LogMessage($"    CommodoreSoftware MediaExtensionsAllowed: {string.Join(',', configuration.FloppyResolverSettings.CommodoreSoftware.MediaExtensionsAllowed)}");
                    this.Logger.LogMessage($"    CommodoreSoftware IgnoredSearchKeywords: {string.Join(',', configuration.FloppyResolverSettings.CommodoreSoftware.IgnoredSearchKeywords)}");
                }
            }
        }
    }
}
