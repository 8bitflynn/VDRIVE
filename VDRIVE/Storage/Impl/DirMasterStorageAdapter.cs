using System.Diagnostics;
using System.Text.RegularExpressions;
using VDRIVE_Contracts.Enums;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Drive.Impl
{
    public class DirMasterStorageAdapter : StorageAdapterBase, IStorageAdapter
    {
        public DirMasterStorageAdapter(IConfiguration configuration, ILogger logger)
        {
            this.Configuration = configuration;
            this.Logger = logger;
        }

        public LoadResponse Load(LoadRequest loadRequest, IFloppyResolver floppyResolver, out byte[] payload)
        {
            try
            {
                byte responseCode = 0xff; // success
                string filename = new string(loadRequest.FileName).TrimEnd('\0');

                if (filename.StartsWith("$")) // TODO: implement wildcards / filtering
                {
                    payload = LoadDirectory(loadRequest, floppyResolver.GetInsertedFloppyInfo(), floppyResolver.GetInsertedFloppyPointer(), out responseCode);
                }
                else if (filename.StartsWith("*") || filename.StartsWith(":*")) // SX64 Commodore->Run Stop Combo
                {
                    // hack to allow loading of PRG files directly for now 
                    // by just mounting the PRG and loading with "*"
                    // thought about wrapping in D64 but seems unnecessary overhead
                    // and its less steps for user
                    FloppyPointer floppyPointer = floppyResolver.GetInsertedFloppyPointer();
                    if (!floppyPointer.Equals(default(FloppyPointer)) && floppyPointer.ImagePath.ToLower().EndsWith(".prg"))
                    {
                        payload = File.ReadAllBytes(floppyResolver.GetInsertedFloppyPointer().ImagePath); // load PRG
                    }
                    else
                    {
                        string[] rawLines = LoadRawDirectoryLines(floppyResolver.GetInsertedFloppyPointer());
                        string lineWithFirstFile = rawLines[1]; // get first file

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
                Logger.LogMessage(exception.Message, LogSeverity.Error);

                payload = null;
                return BuildLoadResponse(loadRequest, payload, 0x04); // file not found 
            }
        }

        protected byte[] LoadFile(LoadRequest loadRequest, FloppyInfo floppyInfo, FloppyPointer floppyPointer, out byte responseCode)
        {
            string fullPath = Path.Combine(Configuration.TempPath, Configuration.TempFolder);
            if (!Directory.Exists(fullPath)) 
                Directory.CreateDirectory(fullPath);

            string safeName = new string(loadRequest.FileName.TakeWhile(c => c != '\0').ToArray()).ToUpper();
            string outPrgPath = Path.Combine(Configuration.TempPath, Configuration.TempFolder, safeName).Trim();

            if (File.Exists(outPrgPath))
                File.Delete(outPrgPath);         

            string arguments = $"\"{this.Configuration.StorageAdapterSettings.DirMaster.ScriptPath}\" load \"{floppyPointer.ImagePath}\" \"{safeName}\"";

            var psi = new ProcessStartInfo
            {
                FileName = Configuration.StorageAdapterSettings.DirMaster.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.Combine(Configuration.TempPath, Configuration.TempFolder)
            };

            using (var process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (!string.IsNullOrWhiteSpace(output))
                    Logger.LogMessage(output.Trim());
                if (!string.IsNullOrWhiteSpace(error))
                    Logger.LogMessage(error.Trim(), LogSeverity.Error);

                // TODO: fix this to work with any extension
                string fulloutputPath = outPrgPath + ".prg";
                if (File.Exists(fulloutputPath))
                {
                    Logger.LogMessage($"File extracted: {outPrgPath}");
                    responseCode = 0xff; // success
                    return File.ReadAllBytes(fulloutputPath);
                }
                else
                {
                    Logger.LogMessage($"{safeName}.prg not found in temp directory.", LogSeverity.Error);
                    responseCode = 0x04; // file not found
                    return null;
                }
            }
        }

        protected byte[] LoadDirectory(LoadRequest loadRequest, FloppyInfo floppyInfo, FloppyPointer floppyPointer, out byte responseCode)
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

            if (dirPrgBytes != null && dirPrgBytes.Length > 0 && rawLines.Length > 0)
            {
                Logger.LogMessage($"$ created successfully: {dirPrgPath}");
                responseCode = 0xff;
                return dirPrgBytes;
            }

            responseCode = 0x04; // file not found
            return null;
        }

        private string[] LoadRawDirectoryLines(FloppyPointer floppyPointer)
        {
            string arguments = $"\"{Configuration.StorageAdapterSettings.DirMaster.ScriptPath}\" dir \"{floppyPointer.ImagePath}\"";
            var psi = new ProcessStartInfo
            {
                FileName = Configuration.StorageAdapterSettings.DirMaster.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using (var process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (!string.IsNullOrWhiteSpace(output))
                    Logger.LogMessage(output.Trim());
                if (!string.IsNullOrWhiteSpace(error))
                    Logger.LogMessage(error.Trim(), LogSeverity.Error);

                string[] rawLines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                return rawLines;
            }
        }

        public SaveResponse Save(SaveRequest saveRequest, IFloppyResolver floppyResolver, byte[] payload)
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

            string scriptPath = this.Configuration.StorageAdapterSettings.DirMaster.ScriptPath;
            string command = "save";
            string diskPath = floppyResolver.GetInsertedFloppyPointer().ImagePath;
            string filename = safeName.ToUpper();
            string extensionNoDot = Path.GetExtension(filename); // PRG, SEQ, USR
            if (string.IsNullOrWhiteSpace(extensionNoDot))
            {
                extensionNoDot = "PRG";
            }

            string arguments = $"\"{scriptPath}\" {command} \"{diskPath}\" \"{tempPrgPath}\" \"{extensionNoDot}\"";

            var psi = new ProcessStartInfo
            {
                FileName = Configuration.StorageAdapterSettings.DirMaster.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.Combine(Configuration.TempPath, Configuration.TempFolder)
            };

            using (var process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (!string.IsNullOrWhiteSpace(output))
                    Logger.LogMessage(output.Trim());
                if (!string.IsNullOrWhiteSpace(error))
                    Logger.LogMessage(error.Trim(), LogSeverity.Error);
            }

            bool success = File.Exists(tempPrgPath);
            if (success)
            {
                // TODO: parse errors and return if needed
                Logger.LogMessage($"File written to Image: {safeName}");
                File.Delete(tempPrgPath); // cleanup temp file
            }            

            return saveResponse;
        }
    }
}
