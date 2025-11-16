using System.Text.RegularExpressions;
using VDRIVE.Drive;
using VDRIVE_Contracts.Enums;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Storage.Impl
{
    public class ViceStorageAdapter : StorageAdapterBase, IStorageAdapter
    {
        public ViceStorageAdapter(IProcessRunner processRunner, IConfiguration configuration, ILogger logger)
        {
            this.ProcessRunner = processRunner;
            this.Configuration = configuration;
            this.Logger = logger;
        }

        SaveResponse IStorageAdapter.Save(SaveRequest saveRequest, IFloppyResolver floppyResolver, byte[] payload)
        {
            try
            {
                FloppyPointer floppyPointer = floppyResolver.GetInsertedFloppyPointer();
                if (floppyPointer.Equals(default(FloppyPointer)))
                {
                    payload = null;
                    return new SaveResponse { ResponseCode = 0x04 }; // file not foudn                    
                }

                byte[] destPtrFileData = new byte[payload.Length + 2];
                destPtrFileData[0] = saveRequest.TargetAddressLo;
                destPtrFileData[1] = saveRequest.TargetAddressHi;
                payload.CopyTo(destPtrFileData, 2);

                string fullPath = Path.Combine(Configuration.TempPath, Configuration.TempFolder, Thread.CurrentThread.ManagedThreadId.ToString());
                if (!Directory.Exists(fullPath))
                    Directory.CreateDirectory(fullPath);

                string safeName = new string(saveRequest.FileName.TakeWhile(c => c != '\0').ToArray()).ToLowerInvariant();
                string tempPrgPath = Path.Combine(fullPath, safeName);

                File.WriteAllBytes(tempPrgPath, destPtrFileData);

                string fileSpec = $"@8:{safeName}";
                string imagePath = floppyResolver.GetInsertedFloppyPointer().ImagePath;

                bool isVice3 = Configuration.StorageAdapterSettings.Vice.Version.StartsWith("3.");
                bool forceDeleteFirst = Configuration.StorageAdapterSettings.Vice.ForceDeleteFirst;

                string arguments = $"\"{imagePath}\"";

                if (isVice3 && forceDeleteFirst)
                {
                    arguments += $" -delete \"{fileSpec}\""; // 2.4 behavior
                }
                arguments += $" -write \"{tempPrgPath}\" \"{fileSpec}\" -quit";

                RunProcessParameters runProcessParameters = new RunProcessParameters();
                runProcessParameters.ImagePath = imagePath;
                runProcessParameters.Arguments = arguments;
                runProcessParameters.ExecutablePath = this.Configuration.StorageAdapterSettings.Vice.ExecutablePath;
                runProcessParameters.LockType = LockType.Write;
                runProcessParameters.LockTimeoutSeconds = this.Configuration.StorageAdapterSettings.LockTimeoutSeconds;

                RunProcessResult runProcessResult = this.ProcessRunner.RunProcess(runProcessParameters);

                SaveResponse saveResponse = new SaveResponse();
                if (!runProcessResult.HasError)
                {
                    saveResponse.ResponseCode = (byte)0xff;
                    Logger.LogMessage($"File written to Image: {safeName}");
                }
                else
                {
                    saveResponse.ResponseCode = (byte)0x04;
                }

                if (File.Exists(tempPrgPath))
                {
                    File.Delete(tempPrgPath); // cleanup temp file
                }

                return saveResponse;
            }
            catch (Exception exception)
            {
                Logger.LogMessage(exception.Message, LogSeverity.Error);

                payload = null;
                return new SaveResponse { ResponseCode = 0x04 }; 
            }           
        }

        LoadResponse IStorageAdapter.Load(LoadRequest loadRequest, IFloppyResolver floppyResolver, out byte[] payload)
        {
            try
            {
                FloppyPointer floppyPointer = floppyResolver.GetInsertedFloppyPointer();
                if (floppyPointer.Equals(default(FloppyPointer)))
                {
                    payload = null;
                    return BuildLoadResponse(loadRequest, null, 0x04); // file not found    
                }


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
                    
                    if (!floppyPointer.Equals(default) && floppyPointer.ImagePath.ToLower().EndsWith(".prg"))
                    {
                        payload = File.ReadAllBytes(floppyResolver.GetInsertedFloppyPointer().ImagePath);
                    }
                    else
                    {
                        string[] rawLines = LoadRawDirectoryLines(floppyResolver.GetInsertedFloppyPointer());
                        string lineWithFirstFile = rawLines.FirstOrDefault(rawLine => rawLine.ToLower().Contains("prg"));

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

        private byte[] LoadFile(LoadRequest loadRequest, FloppyInfo floppyInfo, FloppyPointer floppyPointer, out byte responseCode)
        {
            if (floppyInfo.Equals(default) || floppyPointer.Equals(default)
                || string.IsNullOrWhiteSpace(floppyPointer.ImagePath))
            {
                responseCode = 0x04; // file not found
                return null;
            }

            string fullPath = Path.Combine(Configuration.TempPath, Configuration.TempFolder, Thread.CurrentThread.ManagedThreadId.ToString());
            if (!Directory.Exists(fullPath)) ;
            Directory.CreateDirectory(fullPath);          

            string safeName = new string(loadRequest.FileName.TakeWhile(c => c != '\0').ToArray()).ToLowerInvariant();
            string outPrgPath = Path.Combine(fullPath, safeName).Trim();
            outPrgPath = outPrgPath.Replace(@"/", "_");
            if (File.Exists(outPrgPath))
                File.Delete(outPrgPath);

            string fileSpec = $"@8:{safeName}";
            string arguments = $"\"{new string(floppyPointer.ImagePath)}\" -read \"{fileSpec}\" \"{outPrgPath}\" -quit";

            RunProcessParameters runProcessParameters = new RunProcessParameters();
            runProcessParameters.ImagePath = floppyPointer.ImagePath;
            runProcessParameters.Arguments = arguments;
            runProcessParameters.ExecutablePath = this.Configuration.StorageAdapterSettings.Vice.ExecutablePath;
            runProcessParameters.LockType = LockType.Read;
            runProcessParameters.LockTimeoutSeconds = this.Configuration.StorageAdapterSettings.LockTimeoutSeconds;

            RunProcessResult runProcessResult = this.ProcessRunner.RunProcess(runProcessParameters);

            if (!runProcessResult.Output.ToLower().Contains("reading file"))
            {
                // TODO: map real codes from c1541.exe as they appear to be different?
                responseCode = 0x04; // file not found
                return null;
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

        private byte[] LoadDirectory(LoadRequest loadRequest, FloppyInfo floppyInfo, FloppyPointer floppyPointer, out byte responseCode)
        {
            string fullPath = Path.Combine(Configuration.TempPath, Configuration.TempFolder, Thread.CurrentThread.ManagedThreadId.ToString());
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
            string arguments = $"\"{new string(floppyPointer.ImagePath)}\" -dir";

            RunProcessParameters runProcessParameters = new RunProcessParameters();
            runProcessParameters.ImagePath = floppyPointer.ImagePath;
            runProcessParameters.Arguments = arguments;
            runProcessParameters.ExecutablePath = this.Configuration.StorageAdapterSettings.Vice.ExecutablePath;
            runProcessParameters.LockType = LockType.Read;
            runProcessParameters.LockTimeoutSeconds = this.Configuration.StorageAdapterSettings.LockTimeoutSeconds;

            RunProcessResult runProcessResult = this.ProcessRunner.RunProcess(runProcessParameters);

            string[] rawLines = rawLines = runProcessResult.Output
                  .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            
            if (this.Configuration.StorageAdapterSettings.Vice.Version.StartsWith("3.")) 
            {
                // Vice39 c1541.exe just contains some extra info
                var filteredLines = rawLines
                    .Skip(3)                     // Skip first 3 lines
                    .Take(rawLines.Length - 4)     // Take all but the last line (1 + 3 skipped)
                    .Select(line => line.Trim()) // Optional: clean up whitespace
                    .ToList();
                return filteredLines.ToArray();
            }

            return rawLines;
        }     
    }
}
