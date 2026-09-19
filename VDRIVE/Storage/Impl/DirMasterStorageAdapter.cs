using System.Text.RegularExpressions;
using VDRIVE_Contracts.Enums;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;
using VDRIVE_Contracts.Structures.Http;

namespace VDRIVE.Drive.Impl
{
    public class DirMasterStorageAdapter : StorageAdapterBase, IStorageAdapter
    {
        public DirMasterStorageAdapter(IProcessRunner processRunner, IConfiguration configuration, ILogger logger)
        {
            this.ProcessRunner = processRunner;
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
            if (floppyInfo.Equals(default) || floppyPointer.Equals(default)
                || string.IsNullOrWhiteSpace(floppyPointer.ImagePath))
            {
                responseCode = 0x04; // file not found
                return null;
            }

            string fullPath = Path.Combine(Configuration.TempPath, Configuration.TempFolder, Thread.CurrentThread.ManagedThreadId.ToString());
            if (!Directory.Exists(fullPath)) ;
            Directory.CreateDirectory(fullPath);

            string safeName = new string(loadRequest.FileName.TakeWhile(c => c != '\0').ToArray()).ToUpper();
            string outPrgPath = Path.Combine(Configuration.TempPath, Configuration.TempFolder, safeName).Trim();

            if (File.Exists(outPrgPath))
                File.Delete(outPrgPath);         

            string arguments = $"\"{this.Configuration.StorageAdapterSettings.DirMaster.ScriptPath}\" load \"{floppyPointer.ImagePath}\" \"{safeName}\" {Configuration.StorageAdapterSettings.DirMaster.CBMDiskPath}";

            RunProcessParameters runProcessParameters = new RunProcessParameters();
            runProcessParameters.ImagePath = floppyPointer.ImagePath;
            runProcessParameters.Arguments = arguments;
            runProcessParameters.ExecutablePath = this.Configuration.StorageAdapterSettings.DirMaster.ExecutablePath;
            runProcessParameters.LockType = LockType.Read;
            runProcessParameters.LockTimeoutSeconds = this.Configuration.StorageAdapterSettings.LockTimeoutSeconds;

            RunProcessResult runProcessResult = this.ProcessRunner.RunProcess(runProcessParameters);
            
            if (File.Exists(outPrgPath))
            {
                Logger.LogMessage($"File extracted: {outPrgPath}");
                responseCode = 0xff; // success
                return File.ReadAllBytes(outPrgPath);
            }
            else
            {
                Logger.LogMessage($"{safeName} not found in temp directory.", LogSeverity.Error);
                responseCode = 0x04; // file not found
                return null;
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
            DateTime dirStart = DateTime.Now;

            string arguments = $"\"{Configuration.StorageAdapterSettings.DirMaster.ScriptPath}\" dir \"{floppyPointer.ImagePath}\" {Configuration.StorageAdapterSettings.DirMaster.CBMDiskPath}";

            RunProcessParameters runProcessParameters = new RunProcessParameters();
            runProcessParameters.ImagePath = floppyPointer.ImagePath;
            runProcessParameters.Arguments = arguments;
            runProcessParameters.ExecutablePath = this.Configuration.StorageAdapterSettings.DirMaster.ExecutablePath;
            runProcessParameters.LockType = LockType.Read;
            runProcessParameters.LockTimeoutSeconds = this.Configuration.StorageAdapterSettings.LockTimeoutSeconds;

            RunProcessResult runProcessResult = this.ProcessRunner.RunProcess(runProcessParameters);

            Logger.LogMessage($"[LoadRawDirectoryLines] dir took {(DateTime.Now - dirStart).TotalMilliseconds}ms");

            // Check for network errors
            if (runProcessResult.HasError)
            {
                Logger.LogMessage($"[LoadRawDirectoryLines] error: {runProcessResult.Error}", LogSeverity.Error);
                return new string[0];
            }

            string[] rawLines = runProcessResult.Output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            return rawLines;          
        }

        public SaveResponse Save(SaveRequest saveRequest, IFloppyResolver floppyResolver, byte[] payload)
        {
            if (this.Configuration.StorageAdapterSettings.Readonly)
            {
                payload = null;
                return new SaveResponse { ResponseCode = 0x04 }; // for now return file not found
            }

            SaveResponse saveResponse = new SaveResponse();
            saveResponse.ResponseCode = 0xff;

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

            string scriptPath = this.Configuration.StorageAdapterSettings.DirMaster.ScriptPath;
            string command = "save";
            string imagePath = floppyResolver.GetInsertedFloppyPointer().ImagePath;
            string filename = safeName.ToUpper();
            string extensionNoDot = Path.GetExtension(filename); // PRG, SEQ, USR
            if (string.IsNullOrWhiteSpace(extensionNoDot))
            {
                extensionNoDot = "PRG";
            }

            string arguments = $"\"{scriptPath}\" {command} \"{imagePath}\" \"{tempPrgPath}\" \"{extensionNoDot}\"";

            RunProcessParameters runProcessParameters = new RunProcessParameters();
            runProcessParameters.ImagePath = imagePath;
            runProcessParameters.Arguments = arguments;
            runProcessParameters.ExecutablePath = this.Configuration.StorageAdapterSettings.Vice.ExecutablePath;
            runProcessParameters.LockType = LockType.Write;
            runProcessParameters.LockTimeoutSeconds = this.Configuration.StorageAdapterSettings.LockTimeoutSeconds;

            RunProcessResult runProcessResult = this.ProcessRunner.RunProcess(runProcessParameters);

            //TODO: add better error handling

            bool success = File.Exists(tempPrgPath);
            if (success)
            {
                // TODO: parse errors and return if needed
                Logger.LogMessage($"File written to Image: {safeName}");
                File.Delete(tempPrgPath); // cleanup temp file
            }

            return saveResponse;
        }

        public CreateFloppyResponse CreateFloppyImage(CreateFloppyRequest createFloppyRequest)
        {
            try
            {
                // Parse inputs / defaults
                string diskName = (createFloppyRequest?.Filename ?? "NEW_DISK").Trim();
                if (string.IsNullOrWhiteSpace(diskName)) diskName = "NEW_DISK";

                string mediaType = (createFloppyRequest?.MediaType ?? "D64").ToUpperInvariant();
                string label = createFloppyRequest?.InternalName ?? diskName;

                // Map media type -> extension
                string ext = mediaType switch
                {
                    "D81" => ".d81",
                    "G64" => ".g64",
                    _ => ".d64"
                };

                // Target folder: prefer configured NewFloppyPath, fallback to temp created_images
                string baseDir = this.Configuration.StorageAdapterSettings?.NewFloppyPath;
                if (string.IsNullOrWhiteSpace(baseDir))
                {
                    baseDir = Path.Combine(this.Configuration.TempPath ?? Path.GetTempPath(), this.Configuration.TempFolder ?? "vdrive_tmp", "created_images");
                }

                if (!Directory.Exists(baseDir))
                    Directory.CreateDirectory(baseDir);

                // Build safe filename and ensure uniqueness
                string safeName = string.Join("_", diskName.Split(Path.GetInvalidFileNameChars())).Trim();
                if (string.IsNullOrWhiteSpace(safeName)) safeName = "NEW_DISK";

                string candidateName = safeName + ext;
                string fullPath = Path.Combine(baseDir, candidateName);
                int suffix = 1;
                while (File.Exists(fullPath))
                {
                    fullPath = Path.Combine(baseDir, $"{safeName}_{suffix}{ext}");
                    suffix++;
                }

                // DirMaster script + executable
                string scriptPath = this.Configuration.StorageAdapterSettings?.DirMaster?.ScriptPath;
                string dirMasterExe = this.Configuration.StorageAdapterSettings?.DirMaster?.ExecutablePath;

                if (string.IsNullOrWhiteSpace(scriptPath) || string.IsNullOrWhiteSpace(dirMasterExe) || !File.Exists(dirMasterExe))
                {
                    Logger.LogMessage("[CreateFloppyImage] DirMaster script or executable not configured/found", LogSeverity.Error);
                    return new CreateFloppyResponse
                    {
                        Success = false,
                        FloppyInfo = null,
                        ErrorMessage = "DirMaster script or executable not configured/found"
                    };
                }

                // Some DirMaster scripts expect media/type tokens differently; use mediaType lowercase token for script
                string mediaArg = mediaType.ToLowerInvariant();

                // Limit label length similar to c1541 constraints
                string safeLabel = label.Length > 16 ? label.Substring(0, 16) : label;

                // Build arguments: "<script> create "<fullPath>" <mediaArg> "<label>""
                string arguments = $"\"{scriptPath}\" create \"{fullPath}\" {mediaArg} \"{safeLabel}\"";

                var runParams = new RunProcessParameters
                {
                    ExecutablePath = dirMasterExe,
                    ImagePath = fullPath,
                    Arguments = arguments,
                    LockType = LockType.Write,
                    LockTimeoutSeconds = this.Configuration.StorageAdapterSettings?.LockTimeoutSeconds ?? 30
                };

                Logger.LogMessage($"[CreateFloppyImage] Running DirMaster create: {arguments}", LogSeverity.Verbose);
                RunProcessResult runProcessResult = this.ProcessRunner.RunProcess(runParams);

                if (runProcessResult == null)
                {
                    Logger.LogMessage("[CreateFloppyImage] DirMaster did not run (lock timeout or runner returned null)", LogSeverity.Error);
                    return new CreateFloppyResponse
                    {
                        Success = false,
                        FloppyInfo = null,
                        ErrorMessage = "DirMaster did not run (lock timeout or runner error)"
                    };
                }

                if (runProcessResult.HasError)
                {
                    Logger.LogMessage($"[CreateFloppyImage] DirMaster error: {runProcessResult.Error}", LogSeverity.Error);
                    return new CreateFloppyResponse
                    {
                        Success = false,
                        FloppyInfo = null,
                        ErrorMessage = $"DirMaster create failed: {runProcessResult.Error ?? runProcessResult.Output}"
                    };
                }

                // Verify the created image exists
                if (!File.Exists(fullPath))
                {
                    Logger.LogMessage($"[CreateFloppyImage] Image file not found after DirMaster: {fullPath}", LogSeverity.Error);
                    return new CreateFloppyResponse
                    {
                        Success = false,
                        FloppyInfo = null,
                        ErrorMessage = "DirMaster did not create the image file"
                    };
                }

                // Build FloppyInfo for resolver (resolver will assign ID on insert)
                FloppyInfo floppyInfo = new FloppyInfo();
                floppyInfo.IdLo = 0;
                floppyInfo.IdHi = 0;
                string displayName = Path.GetFileName(fullPath);
                if (displayName.Length > 64) displayName = displayName.Substring(0, 64);
                floppyInfo.ImageNameLength = (byte)displayName.Length;
                floppyInfo.ImageName = new char[64];
                displayName.ToUpperInvariant().ToCharArray().CopyTo(floppyInfo.ImageName, 0);

                return new CreateFloppyResponse
                {
                    Success = true,
                    FloppyInfo = floppyInfo,
                    ErrorMessage = null
                };
            }
            catch (Exception ex)
            {
                Logger.LogMessage($"[CreateFloppyImage] Failed: {ex.Message}", LogSeverity.Error);
                return new CreateFloppyResponse
                {
                    Success = false,
                    FloppyInfo = null,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}
