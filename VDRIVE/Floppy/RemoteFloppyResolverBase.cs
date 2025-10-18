using System.IO.Compression;
using System.Net;
using VDRIVE_Contracts.Structures;

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
                    this.Logger.LogMessage($"Download complete ({(fileBytes != null ? fileBytes.Length : 0)} bytes): " + new string(this.InsertedFloppyInfo.Value.ImageName));

                    return fileBytes;
                }
                catch (Exception ex)
                {
                    this.Logger.LogMessage("Download failed: " + ex.Message);
                    return null;
                }
            }
        }

        protected void Decompress(byte[] zipBytes)
        {
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

                    if (this.InsertedFloppyPointer.HasValue)
                    {
                        if (!this.Configuration.MediaExtensionAllowed.Any(ir => fullFilePath.ToLower().EndsWith(ir)))
                        {
                            continue;
                        }

                        FloppyInfo tempFloppyInfo = this.InsertedFloppyInfo.Value;
                        tempFloppyInfo.ImageName = Path.GetFileName(fullFilePath).ToCharArray();
                        this.InsertedFloppyInfo = tempFloppyInfo; // update to extracted file name

                        FloppyPointer tempFloppyPointer = this.InsertedFloppyPointer.Value;
                        tempFloppyPointer.ImagePath = fullFilePath;
                        this.InsertedFloppyPointer = tempFloppyPointer; // update to extracted file path                        
                    }
                }
            }

            return;
        }
    }
}
