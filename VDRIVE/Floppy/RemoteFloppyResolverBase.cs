using System.IO.Compression;
using System.Net;

namespace VDRIVE.Floppy
{
    public abstract class RemoteFloppyResolverBase : FloppyResolverBase
    {
        protected byte[] DownloadFile(string url)
        {
            using (HttpClient httpClient = new HttpClient())
            {
                try
                {
                    string decodedUrl = WebUtility.HtmlDecode(url);

                    HttpResponseMessage httpResponseMessage = httpClient.GetAsync(decodedUrl).GetAwaiter().GetResult();
                    httpResponseMessage.EnsureSuccessStatusCode();

                    byte[] fileBytes = httpResponseMessage.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    this.Logger.LogMessage($"Download complete ({(fileBytes != null ? fileBytes.Length : 0)} bytes): " + new string(this.InsertedFloppyInfo.ImageName));

                    return fileBytes;
                }
                catch (Exception ex)
                {
                    this.Logger.LogMessage("Download failed: " + ex.Message);
                    return null;
                }
            }
        }

        protected IEnumerable<string> DecompressArchive(byte[] zipBytes)
        {
            List<string> extractedFullFilePaths = new List<string>();

            using (var zipStream = new MemoryStream(zipBytes))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    // directory (may not be C64 software)
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }

                    this.Logger.LogMessage($"Extracting: {entry.FullName} {entry.Length} bytes");

                    // Skip directories
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    // Build full output path
                    string fullFilePath = Path.Combine(this.Configuration.TempPath, this.Configuration.TempFolder, entry.FullName);

                    // Ensure directory exists
                    if (!Directory.Exists(Path.GetDirectoryName(fullFilePath)))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(fullFilePath));
                    }

                    // Extract file
                    entry.ExtractToFile(fullFilePath, overwrite: true);

                    extractedFullFilePaths.Add(fullFilePath);
                }
            }

            return extractedFullFilePaths;
        }
    }
}
