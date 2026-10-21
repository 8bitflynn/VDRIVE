using Microsoft.Extensions.Configuration;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE.Configuration
{
    public class ConfigurationBuilder : VDRIVE_Contracts.Interfaces.IConfigurationBuilder
    {
        public ConfigurationBuilder(ILogger logger)
        {
            this.Logger = logger;
        }
        private readonly VDRIVE_Contracts.Interfaces.ILogger Logger;

        public VDRIVE_Contracts.Interfaces.IConfiguration BuildConfiguration()
        {
            string basePath = Directory.GetCurrentDirectory();
            JsonNode settings = ReadJson(Path.Combine(basePath, "appsettings.json"));

            // appsettings.private.json (optional, in .gitignore) holds this machine's own values - tokens, folders,
            // tool paths - so they never go to GitHub. Anything in it replaces the same setting in appsettings.json;
            // a list in it (AllowedAuthTokens, SearchPaths...) replaces the whole list, so the examples in
            // appsettings.json never mix in.
            string privatePath = Path.Combine(basePath, "appsettings.private.json");
            if (File.Exists(privatePath))
            {
                Merge(settings, ReadJson(privatePath));
                this.Logger.LogMessage("Loaded appsettings.private.json");
            }

            IConfigurationRoot? configRoot = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(settings.ToJsonString())))
                .Build();
            VDRIVE_Contracts.Interfaces.IConfiguration configuration = configRoot.GetSection("AppSettings").Get<VDRIVE_Contracts.Structures.Configuration>();

            if (configuration == null)
            {
                this.Logger.LogMessage("Could not load the AppSettings", VDRIVE_Contracts.Enums.LogSeverity.Critical);
                return null;
            }          

            this.EnsureDefaultValues(configuration);

            return configuration;
        }

        private static JsonNode ReadJson(string path)
        {
            // appsettings files have // comments and the odd trailing comma
            var documentOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            return JsonNode.Parse(File.ReadAllText(path), null, documentOptions) ?? new JsonObject();
        }

        // objects merge key by key; values and lists from 'overrides' replace what 'target' has
        private static void Merge(JsonNode target, JsonNode overrides)
        {
            if (target is not JsonObject targetObject || overrides is not JsonObject overrideObject)
                return;

            foreach (KeyValuePair<string, JsonNode?> entry in overrideObject.ToList())
            {
                JsonNode? value = entry.Value;
                if (value is JsonObject && targetObject[entry.Key] is JsonObject existing)
                {
                    Merge(existing, value);
                }
                else
                {
                    overrideObject.Remove(entry.Key);   // a node can only have one parent
                    targetObject[entry.Key] = value;
                }
            }
        }

        private void EnsureDefaultValues(VDRIVE_Contracts.Interfaces.IConfiguration configuration)
        {
            if (string.IsNullOrEmpty(configuration.TempPath))
            {
                // use system default 
                // TODO: test on linux
                configuration.TempPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }            
            configuration.ChunkSize = configuration.ChunkSize == 0 ? (ushort)1024 : configuration.ChunkSize;
            configuration.MaxSearchResults = configuration.MaxSearchResults == 0 ? (ushort)18 : configuration.MaxSearchResults;
            configuration.SearchPageSize = configuration.SearchPageSize == 0 ? 15 : configuration.SearchPageSize;            
            configuration.WebSearchPageSize = configuration.WebSearchPageSize == 0 ? 20 : configuration.WebSearchPageSize;
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

            if (string.IsNullOrWhiteSpace(configuration.LoggingLevel))
            {
                this.Logger.LogMessage("LoggingLevel is not set in configuration");
                return false;
            }

            if (configuration.SessionTimeoutMinutes == 0)
            {
                this.Logger.LogMessage("SessionTimeoutMinutes is not set in configuration");
                return false;
            }

            if (string.IsNullOrWhiteSpace(configuration.ServerOrClientMode))
            {
                this.Logger.LogMessage("ServerOrClientMode is not set in configuration");
                return false;
            }
            
            if (string.IsNullOrWhiteSpace(configuration.ServerType))
            {
                this.Logger.LogMessage("ServerType is not set in configuration");
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

                case "C64":
                    //return false;
                    break;

                case "HvscPsid":
                    if (configuration.FloppyResolverSettings.HvscPsid == null ||
                       string.IsNullOrWhiteSpace(configuration.FloppyResolverSettings.HvscPsid.BaseURL) ||
                       string.IsNullOrWhiteSpace(configuration.FloppyResolverSettings.HvscPsid.ExecutablePath) ||
                       string.IsNullOrWhiteSpace(configuration.FloppyResolverSettings.HvscPsid.Arguments))
                    {
                        this.Logger.LogMessage("HvscPsid resolver settings are incomplete");
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
            this.Logger.LogMessage($"  AllowedAuthTokens: { (configuration.AllowedAuthTokens.Count == 0 ? "All" : string.Join(", ", configuration.AllowedAuthTokens))}");
            this.Logger.LogMessage($"  StorageAdapter: {configuration.StorageAdapter}");
            this.Logger.LogMessage($"  FloppyResolver: {configuration.FloppyResolver}");   
            this.Logger.LogMessage($"  LoggingLevel: {configuration.LoggingLevel}");
            this.Logger.LogMessage($"  SessionTimeoutMinutes: {configuration.SessionTimeoutMinutes}");
            this.Logger.LogMessage($"  TempPath: {configuration.TempPath}");
            this.Logger.LogMessage($"  LogPath: {(string.IsNullOrWhiteSpace(configuration.LogPath) ? "(logs next to the exe)" : configuration.LogPath)}");
            this.Logger.LogMessage($"  WriteFolder: {VDRIVE.Protocol.PatchStubs.WriteFolderOrDefault(configuration.WriteFolder)}");
            this.Logger.LogMessage($"  TempFolder: {configuration.TempFolder}");
            this.Logger.LogMessage($"  ServerOrClientMode: {configuration.ServerOrClientMode}");

            if (configuration.ServerOrClientMode == "Server")
            {
                this.Logger.LogMessage($"  ServerType: {configuration.ServerType}");
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
            this.Logger.LogMessage($"  SearchPageSize: {configuration.SearchPageSize}");
            this.Logger.LogMessage($"  WebSearchPageSize: {configuration.WebSearchPageSize}");
            this.Logger.LogMessage($"  SearchIntroMessage: {configuration.SearchIntroMessage}");

            // Storage adapter settings
            if (configuration.StorageAdapterSettings != null)
            {
                this.Logger.LogMessage($"  StorageAdapterSettings:");
                this.Logger.LogMessage($"   Readonly: {configuration.StorageAdapterSettings.Readonly}");
                this.Logger.LogMessage($"   NewFloppyPath: {configuration.StorageAdapterSettings.NewFloppyPath}");
                this.Logger.LogMessage($"   GameSavePath: {configuration.StorageAdapterSettings.GameSavePath}");
                this.Logger.LogMessage($"   LockTimeoutSeconds: {configuration.StorageAdapterSettings.LockTimeoutSeconds}");
                
                if (configuration.StorageAdapterSettings.DirMaster != null && configuration.StorageAdapter == "DirMaster")
                {
                    this.Logger.LogMessage($"       DirMaster ExecutablePath: {configuration.StorageAdapterSettings.DirMaster.ExecutablePath}");
                    this.Logger.LogMessage($"       DirMaster ScriptPath: {configuration.StorageAdapterSettings.DirMaster.ScriptPath}");
                    this.Logger.LogMessage($"       DirMaster CBMDiskPath: {configuration.StorageAdapterSettings.DirMaster.CBMDiskPath}");
                }
                if (configuration.StorageAdapterSettings.Vice != null && configuration.StorageAdapter == "Vice")
                {
                    this.Logger.LogMessage($"       Vice ExecutablePath: {configuration.StorageAdapterSettings.Vice.ExecutablePath}");
                    this.Logger.LogMessage($"       Vice Version: {configuration.StorageAdapterSettings.Vice.Version}");
                    this.Logger.LogMessage($"       Vice ForceDeleteFirst: {configuration.StorageAdapterSettings.Vice.ForceDeleteFirst}"); 
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
                    this.Logger.LogMessage($"    Local GroupMultiDiskImages: {configuration.FloppyResolverSettings.Local.GroupMultiDiskImages}");
                    if (configuration.FloppyResolverSettings.Local.DiskMarkers != null)
                    {
                        this.Logger.LogMessage($"    Local DiskMarkers: {string.Join(", ", configuration.FloppyResolverSettings.Local.DiskMarkers)}");
                    }
                    if (configuration.FloppyResolverSettings.Local.CompanionDiskNames != null)
                    {
                        this.Logger.LogMessage($"    Local CompanionDiskNames: {string.Join(", ", configuration.FloppyResolverSettings.Local.CompanionDiskNames)}");
                    }
                }
                if (configuration.FloppyResolverSettings.CommodoreSoftware != null)    // @CS works whatever the default resolver is
                {
                    this.Logger.LogMessage($"    CommodoreSoftware BaseURL: {configuration.FloppyResolverSettings.CommodoreSoftware.BaseURL}");
                    this.Logger.LogMessage($"    CommodoreSoftware MediaExtensionsAllowed: {string.Join(',', configuration.FloppyResolverSettings.CommodoreSoftware.MediaExtensionsAllowed)}");
                    this.Logger.LogMessage($"    CommodoreSoftware IgnoredSearchKeywords: {string.Join(',', configuration.FloppyResolverSettings.CommodoreSoftware.IgnoredSearchKeywords)}");
                }
                if (configuration.FloppyResolverSettings.HvscPsid != null)    // @HVSC too
                {
                    this.Logger.LogMessage($"    Hvsc BaseURL: {configuration.FloppyResolverSettings.HvscPsid.BaseURL}");
                    this.Logger.LogMessage($"    Hvsc LocalPath: {configuration.FloppyResolverSettings.HvscPsid.LocalPath}");
                    this.Logger.LogMessage($"    Hvsc ExecutablePath: {string.Join(',', configuration.FloppyResolverSettings.HvscPsid.ExecutablePath)}");
                    this.Logger.LogMessage($"    Hvsc Arguments: {configuration.FloppyResolverSettings.HvscPsid.Arguments}");
                    this.Logger.LogMessage($"    Hvsc SidRelocPath: {configuration.FloppyResolverSettings.HvscPsid.SidRelocPath}");
                }
            }
        }
    }
}
