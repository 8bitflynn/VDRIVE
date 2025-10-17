using Microsoft.Extensions.Configuration;

namespace VDRIVE.Configuration
{
    public class ConfigurationBuilder : VDRIVE_Contracts.Interfaces.IConfigurationBuilder
    {
        public VDRIVE_Contracts.Interfaces.IConfiguration BuildConfiguration()
        {
            VDRIVE_Contracts.Interfaces.IConfiguration configuration = new VDRIVE_Contracts.Structures.Configuration();

            var configurationBuilder = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
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
            if (ushort.TryParse(configurationBuilder.GetSection("AppSettings:MaxSearchResults").Value, out ushort maxSearchResults))
            {
                configuration.MaxSearchResults = maxSearchResults;
            }
            else
            {
                configuration.MaxSearchResults = 12;
            }

            if (string.IsNullOrEmpty(configuration.TempPath))
            {
                configuration.TempPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) + @"\C64Temp\";
            }

            return configuration;
        }
    }
}
