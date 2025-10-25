using System.Diagnostics;
using System.Text.RegularExpressions;
using VDRIVE_Contracts.Enums;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Drive.Impl
{
    public class Vice24VStorageAdapter : StorageAdapterBase, IStorageAdapter
    {
        public Vice24VStorageAdapter(IConfiguration configuration, ILogger logger)
        {
            Configuration = configuration;
            Logger = logger;
        }

        SaveResponse IStorageAdapter.Save(SaveRequest saveRequest, IFloppyResolver floppyResolver, byte[] payload)
        {
            SaveResponse saveResponse = new SaveResponse();
            saveResponse.ResponseCode = 0xff;

            byte[] destPtrFileData = new byte[payload.Length + 2];
            destPtrFileData[0] = saveRequest.TargetAddressLo;
            destPtrFileData[1] = saveRequest.TargetAddressHi;
            payload.CopyTo(destPtrFileData, 2);

            string fullPath = Path.Combine(Configuration.TempPath, Configuration.TempFolder);
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);

            string safeName = new string(saveRequest.FileName.TakeWhile(c => c != '\0').ToArray()).ToLowerInvariant();
            string tempPrgPath = Path.Combine(fullPath, safeName);

            File.WriteAllBytes(tempPrgPath, destPtrFileData);

            string fileSpec = $"@8:{safeName}";

            var psi = new ProcessStartInfo
            {
                FileName = Configuration.StorageAdapterSettings.Vice24.ExecutablePath,
                Arguments = $"\"{floppyResolver.GetInsertedFloppyPointer().ImagePath}\" -write \"{tempPrgPath}\" \"{fileSpec}\" -quit",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var proc = Process.Start(psi))
            {
                string c1541Out = proc.StandardOutput.ReadToEnd();
                // Writing file -- use this for success check
                proc.WaitForExit();
                Logger.LogMessage(c1541Out.Trim());
            }

            bool success = File.Exists(tempPrgPath);
            if (success)
            {
                // TODO: parse errors and return if needed
                Logger.LogMessage($"File written to Image: {safeName}");
                File.Delete(tempPrgPath); // cleanup temp file
            }
            else
            {
                Logger.LogMessage($"ERROR: Failed to write {safeName} to Image.");
            }

            return saveResponse;
        }


        LoadResponse IStorageAdapter.Load(LoadRequest loadRequest, IFloppyResolver floppyResolver, out byte[] payload)
        {
            try
            {
                byte responseCode = 0xff; // success
                string filename = new string(loadRequest.FileName).TrimEnd('\0');

                if (filename.StartsWith("$")) // TODO: implement wildcards / filtering
                {
                    payload = LoadDirectory(loadRequest, floppyResolver.GetInsertedFloppyInfo(), floppyResolver.GetInsertedFloppyPointer());
                }
                else if (filename.StartsWith("*"))
                {
                    // hack to allow loading of PRG files directly for now 
                    // by just mounting the PRG and loading with "*"
                    // thought about wrapping in D64 but seems unnecessary overhead
                    // and its less steps for user
                    FloppyPointer floppyPointer = floppyResolver.GetInsertedFloppyPointer();
                    if (!floppyPointer.Equals(default(FloppyPointer)) && floppyPointer.ImagePath.ToLower().EndsWith(".prg"))
                    {
                        payload = File.ReadAllBytes(floppyResolver.GetInsertedFloppyPointer().ImagePath);
                    }
                    else
                    {
                        string[] rawLines = LoadRawDirectoryLines(floppyResolver.GetInsertedFloppyPointer());
                        string lineWithFirstFile = rawLines[1];

                        // Match anything inside double quotes, including spaces
                        Match match = Regex.Match(lineWithFirstFile, "\"([^\"]*)\"");
                        if (match.Success)
                        {
                            string extracted = match.Groups[1].Value;

                            string[] tokens = lineWithFirstFile.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            loadRequest.FileName = extracted.ToCharArray();
                            loadRequest.FileNameLength = (byte)extracted.Length;
                            payload = LoadFile(loadRequest, floppyResolver.GetInsertedFloppyInfo(), floppyResolver.GetInsertedFloppyPointer(), out responseCode);
                        }
                        else
                        {
                            payload = null;
                        }
                    }
                }
                else
                {
                    payload = LoadFile(loadRequest, floppyResolver.GetInsertedFloppyInfo(), floppyResolver.GetInsertedFloppyPointer(), out responseCode);
                }

                LoadResponse loadResponse = BuildLoadResponse(loadRequest, payload, responseCode);
                return loadResponse;
            }
            catch (Exception exception)
            {
                Logger.LogMessage(@"ERROR: " + exception.Message, LogSeverity.Error);

                payload = null;
                return BuildLoadResponse(loadRequest, payload, 0x04); // file not found 
            }
        }

        private byte[] LoadFile(LoadRequest loadRequest, FloppyInfo floppyInfo, FloppyPointer floppyPointer, out byte responseCode)
        {
            string fullPath = Path.Combine(Configuration.TempPath, Configuration.TempFolder);
            if (!Directory.Exists(fullPath)) ;
            Directory.CreateDirectory(fullPath);

            string safeName = new string(loadRequest.FileName.TakeWhile(c => c != '\0').ToArray()).ToLowerInvariant();
            string outPrgPath = Path.Combine(Configuration.TempPath, Configuration.TempFolder, safeName).Trim();
            outPrgPath = outPrgPath.Replace(@"/", "_");
            if (File.Exists(outPrgPath))
                File.Delete(outPrgPath);

            string fileSpec = $"@8:{safeName}";

            // execute c1541 to extract the file
            var psi = new ProcessStartInfo
            {
                FileName = Configuration.StorageAdapterSettings.Vice24.ExecutablePath,
                Arguments = $"\"{new string(floppyPointer.ImagePath)}\" -read \"{fileSpec}\" \"{outPrgPath}\" -quit",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var proc = Process.Start(psi))
            {
                string c1541Out = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                Logger.LogMessage(c1541Out.Trim());

                if (!c1541Out.StartsWith("Reading file"))
                {
                    // TODO: map real codes from c1541.exe as they appear to be different?
                    responseCode = 0x04; // file not found
                    return null;
                }
            }

            if (File.Exists(outPrgPath))
            {
                Logger.LogMessage($"File extracted: {outPrgPath}");
                responseCode = 0xff; // success
                return File.ReadAllBytes(outPrgPath);
            }
            else
            {
                Logger.LogMessage($"ERROR: {safeName}.prg not found in temp directory.", LogSeverity.Error);
                responseCode = 0x04; // file not found
                return null;
            }
        }

        private byte[] LoadDirectory(LoadRequest loadRequest, FloppyInfo floppyInfo, FloppyPointer floppyPointer)
        {
            string fullPath = Path.Combine(Configuration.TempPath, Configuration.TempFolder);
            string dirPrgPath = Path.Combine(fullPath, "dir.prg");

            // Ensure temp directory exists          
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            if (File.Exists(dirPrgPath))
            {
                File.Delete(dirPrgPath);
            }

            string[] rawLines = LoadRawDirectoryLines(floppyPointer);

            // convert text directory to PRG
            byte[] dirPrgBytes = BuildDirectoryPrg(rawLines);

            File.WriteAllBytes(dirPrgPath, dirPrgBytes);

            if (dirPrgBytes != null && dirPrgBytes.Length > 0)
            {
                Logger.LogMessage($"$ created successfully: {dirPrgPath}");
                return dirPrgBytes;
            }

            return null;
        }

        private string[] LoadRawDirectoryLines(FloppyPointer floppyPointer)
        {
            // call c1541 to get text directory listing
            var psi = new ProcessStartInfo
            {
                FileName = Configuration.StorageAdapterSettings.Vice24.ExecutablePath,
                Arguments = $"\"{new string(floppyPointer.ImagePath)}\" -dir",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            string[] rawLines;
            using (var proc = Process.Start(psi))
            {
                string allOutput = proc.StandardOutput.ReadToEnd();

                // TODO: check for errors
                // Unknown disk image
                // etc...

                proc.WaitForExit();
                rawLines = allOutput
                    .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            }

            return rawLines;
        }
    }
}
