using System.Text.RegularExpressions;
using VDRIVE.Drive;
using VDRIVE_Contracts.Enums;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;
using VDRIVE_Contracts.Structures.Http;

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
                if (this.Configuration.StorageAdapterSettings.Readonly)
                {
                    payload = null;
                    return new SaveResponse { ResponseCode = 0x04 }; // for now return file not found
                }

                FloppyPointer floppyPointer = floppyResolver.GetInsertedFloppyPointer();

                byte[] destPtrFileData = new byte[payload.Length + 2];
                destPtrFileData[0] = saveRequest.TargetAddressLo;
                destPtrFileData[1] = saveRequest.TargetAddressHi;
                payload.CopyTo(destPtrFileData, 2);

                //// If there's no source (save originated on the C64), persist to configured NewFloppyPath.
                //if (floppyPointer.Equals(default(FloppyPointer)))
                //{
                    string newFloppyBase = this.Configuration.StorageAdapterSettings?.NewFloppyPath;
                //    if (string.IsNullOrWhiteSpace(newFloppyBase))
                //    {
                //        Logger.LogMessage("[Save] NewFloppyPath not configured", LogSeverity.Error);
                //        payload = null;
                //        return new SaveResponse { ResponseCode = 0x04 };
                //    }

                //    try
                //    {
                //        // Build a safe filename from the SaveRequest
                //        string rawName = new string(saveRequest.FileName.TakeWhile(c => c != '\0').ToArray());
                //        string safeName = string.Join("_", rawName.Split(Path.GetInvalidFileNameChars())).Trim();
                //        if (string.IsNullOrWhiteSpace(safeName))
                //            safeName = "NEW_DISK";

                //        if (!safeName.EndsWith(".prg", StringComparison.OrdinalIgnoreCase))
                //            safeName = safeName + ".prg";

                //        // Ensure directory exists
                //        if (!Directory.Exists(newFloppyBase))
                //            Directory.CreateDirectory(newFloppyBase);

                //        string candidatePath = Path.Combine(newFloppyBase, safeName);

                //        // Overwrite if same filename exists (user requested save under this name)
                //        File.WriteAllBytes(candidatePath, destPtrFileData);
                //        Logger.LogMessage($"Saved PRG from C64 to new location: {candidatePath}");
                //        return new SaveResponse { ResponseCode = 0xff };
                //    }
                //    catch (Exception ex)
                //    {
                //        Logger.LogMessage($"[Save] Failed to save new PRG to configured path: {ex.Message}", LogSeverity.Error);
                //        payload = null;
                //        return new SaveResponse { ResponseCode = 0x04 };
                //    }
                //}

                // If the inserted "floppy" is actually a PRG file mounted directly,
                // write back to that PRG path directly instead of writing into a D64 image.
                if (!string.IsNullOrWhiteSpace(floppyPointer.ImagePath)
                    && floppyPointer.ImagePath.ToLowerInvariant().EndsWith(".prg"))
                {
                    try
                    {
                        // Ensure directory exists for network/UNC mapping scenarios
                        string dir = Path.GetDirectoryName(floppyPointer.ImagePath) ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        File.WriteAllBytes(floppyPointer.ImagePath, destPtrFileData);
                        Logger.LogMessage($"File written directly to PRG: {floppyPointer.ImagePath}");
                        return new SaveResponse { ResponseCode = (byte)0xff };
                    }
                    catch (Exception ex)
                    {
                        Logger.LogMessage($"Failed to write directly to PRG '{floppyPointer.ImagePath}': {ex.Message}", LogSeverity.Error);
                        payload = null;
                        return new SaveResponse { ResponseCode = 0x04 };
                    }
                }

                string fullPath = Path.Combine(Configuration.TempPath, Configuration.TempFolder, Thread.CurrentThread.ManagedThreadId.ToString());
                if (!Directory.Exists(fullPath))
                    Directory.CreateDirectory(fullPath);

                string safeNameForImage = new string(saveRequest.FileName.TakeWhile(c => c != '\0').ToArray()).ToLowerInvariant();
                string tempPrgPath = Path.Combine(fullPath, safeNameForImage);

                File.WriteAllBytes(tempPrgPath, destPtrFileData);

                string fileSpec = $"@8:{safeNameForImage}";
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
                    Logger.LogMessage($"File written to Image: {safeNameForImage}");
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
                        string lineWithFirstFile = rawLines?.FirstOrDefault(rawLine => rawLine != null && rawLine.ToLower().Contains("prg"));

                        // no PRG on this disk (e.g. side B of a multi-disk game, or a data-only disk): "file not found"
                        if (lineWithFirstFile == null)
                        {
                            Logger.LogMessage($"[Load] No PRG file on {floppyPointer.ImagePath} - nothing for LOAD \"*\"", LogSeverity.Info);
                            payload = null;
                            return BuildLoadResponse(loadRequest, null, 0x04); // file not found
                        }

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
                            responseCode = 0x04; // file not found (no quoted name on that line)
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
                Logger.LogMessage($"{exception.Message} {exception.ToString()}", LogSeverity.Critical);

                payload = null;
                return BuildLoadResponse(loadRequest, payload, 0x04); // file not found 
            }
        }

        private byte[] LoadFile(LoadRequest loadRequest, FloppyInfo floppyInfo, FloppyPointer floppyPointer, out byte responseCode)
        {
            DateTime methodStart = DateTime.Now;
            string requestedFileName = new string(loadRequest.FileName.TakeWhile(c => c != '\0').ToArray());
            Logger.LogMessage($"[LoadFile] Starting for '{requestedFileName}' at {methodStart:HH:mm:ss.fff}");

            if (floppyInfo.Equals(default) || floppyPointer.Equals(default)
                || string.IsNullOrWhiteSpace(floppyPointer.ImagePath))
            {
                responseCode = 0x04; // file not found
                return null;
            }

            string fullPath = Path.Combine(Configuration.TempPath, Configuration.TempFolder, Thread.CurrentThread.ManagedThreadId.ToString());
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);

            string safeName = requestedFileName.ToLowerInvariant();

            // Sanitize filename to ensure it's valid for filesystem
            string sanitizedFileName = string.Join("_", safeName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(sanitizedFileName))
                sanitizedFileName = "unnamed_file";

            string outPrgPath = Path.Combine(fullPath, sanitizedFileName);
            if (File.Exists(outPrgPath))
                File.Delete(outPrgPath);

            // Try 1: Attempt direct load with filename as-is (fast path - works for most files)
            Logger.LogMessage($"[LoadFile] Attempting direct extraction of '{safeName}'");
            DateTime extractStart = DateTime.Now;

            byte[] result = TryExtractFile(floppyPointer, safeName, outPrgPath, out bool success);
            Logger.LogMessage($"[LoadFile] Direct extraction took {(DateTime.Now - extractStart).TotalMilliseconds}ms, success: {success}");

            if (success)
            {
                Logger.LogMessage($"[LoadFile] Total time (fast path): {(DateTime.Now - methodStart).TotalMilliseconds}ms");
                responseCode = 0xff;
                return result;
            }

            // Try 2: If direct load failed, search directory for filename with trailing spaces (slow path)
            Logger.LogMessage($"[LoadFile] Direct load failed, searching directory for '{safeName}'");
            DateTime findStart = DateTime.Now;

            string actualFileName = FindActualFileName(floppyPointer, safeName);
            Logger.LogMessage($"[LoadFile] Directory search took {(DateTime.Now - findStart).TotalMilliseconds}ms, result: '{actualFileName ?? "NULL"}'");

            if (actualFileName == null)
            {
                Logger.LogMessage($"[LoadFile] File not found in directory: '{safeName}'", LogSeverity.Error);
                responseCode = 0x04;
                return null;
            }

            // Try 3: Extract with the actual filename from directory
            Logger.LogMessage($"[LoadFile] Attempting extraction with actual filename: '{actualFileName}'");
            extractStart = DateTime.Now;

            result = TryExtractFile(floppyPointer, actualFileName, outPrgPath, out success);
            Logger.LogMessage($"[LoadFile] Extraction with actual filename took {(DateTime.Now - extractStart).TotalMilliseconds}ms, success: {success}");

            if (success)
            {
                Logger.LogMessage($"[LoadFile] Total time (slow path): {(DateTime.Now - methodStart).TotalMilliseconds}ms");
                responseCode = 0xff;
                return result;
            }

            Logger.LogMessage($"[LoadFile] All extraction attempts failed for '{safeName}'", LogSeverity.Error);
            responseCode = 0x04;
            return null;
        }

        private byte[] TryExtractFile(FloppyPointer floppyPointer, string fileName, string outPrgPath, out bool success)
        {
            // Verify network path is accessible before attempting extraction
            if (!VerifyImagePathAccessible(floppyPointer.ImagePath))
            {
                Logger.LogMessage($"[TryExtractFile] Image path not accessible: '{floppyPointer.ImagePath}'", LogSeverity.Error);
                success = false;
                return null;
            }

            string fileSpec = $"@8:{fileName}";
            string arguments = $"\"{floppyPointer.ImagePath}\" -read \"{fileSpec}\" \"{outPrgPath}\" -quit";

            RunProcessParameters runProcessParameters = new RunProcessParameters();
            runProcessParameters.ImagePath = floppyPointer.ImagePath;
            runProcessParameters.Arguments = arguments;
            runProcessParameters.ExecutablePath = this.Configuration.StorageAdapterSettings.Vice.ExecutablePath;
            runProcessParameters.LockType = LockType.Read;
            runProcessParameters.LockTimeoutSeconds = this.Configuration.StorageAdapterSettings.LockTimeoutSeconds;

            RunProcessResult runProcessResult = this.ProcessRunner.RunProcess(runProcessParameters);

            if (!runProcessResult.Output.ToLower().Contains("reading file"))
            {
                // Check if error mentions network
                if (runProcessResult.Error?.Contains("network", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Logger.LogMessage($"[TryExtractFile] Network error detected: {runProcessResult.Error}", LogSeverity.Error);
                }
                success = false;
                return null;
            }

            if (File.Exists(outPrgPath))
            {
                Logger.LogMessage($"[TryExtractFile] File extracted successfully: {new FileInfo(outPrgPath).Length} bytes");
                success = true;
                return File.ReadAllBytes(outPrgPath);
            }

            success = false;
            return null;
        }

        private bool VerifyImagePathAccessible(string imagePath)
        {
            try
            {
                // Check if it's a network path
                bool isNetworkPath = imagePath.StartsWith(@"\\") ||
                                     (Uri.TryCreate(imagePath, UriKind.Absolute, out Uri uri) && uri.IsUnc);

                if (isNetworkPath)
                {
                    Logger.LogMessage($"[VerifyImagePath] Checking network path: '{imagePath}'");
                }

                // Attempt to access the file with retry
                int maxRetries = 3;
                int retryDelayMs = 500;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        if (File.Exists(imagePath))
                        {
                            // Try to actually read attributes to ensure real access
                            var fileInfo = new FileInfo(imagePath);
                            var length = fileInfo.Length; // Force actual file access

                            if (attempt > 1)
                            {
                                Logger.LogMessage($"[VerifyImagePath] Successfully accessed after {attempt} attempts");
                            }
                            return true;
                        }
                        else
                        {
                            Logger.LogMessage($"[VerifyImagePath] File does not exist: '{imagePath}'", LogSeverity.Error);
                            return false;
                        }
                    }
                    catch (IOException ioEx) when (ioEx.Message.Contains("network name") ||
                                                  ioEx.HResult == unchecked((int)0x80070035)) // ERROR_BAD_NETPATH
                    {
                        Logger.LogMessage($"[VerifyImagePath] Network error on attempt {attempt}/{maxRetries}: {ioEx.Message}", LogSeverity.Warning);

                        if (attempt < maxRetries)
                        {
                            Thread.Sleep(retryDelayMs);
                            retryDelayMs *= 2; // Exponential backoff
                        }
                        else
                        {
                            Logger.LogMessage($"[VerifyImagePath] Failed to access after {maxRetries} attempts", LogSeverity.Error);
                            return false;
                        }
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.LogMessage($"[VerifyImagePath] Error verifying path '{imagePath}': {ex.Message}", LogSeverity.Error);
                return false;
            }
        }

        private string FindActualFileName(FloppyPointer floppyPointer, string searchName)
        {
            string[] rawLines = LoadRawDirectoryLines(floppyPointer);

            Logger.LogMessage($"[FindActualFileName] Got {rawLines.Length} directory lines, searching for '{searchName}'");

            foreach (string line in rawLines)
            {
                Match match = Regex.Match(line, "\"([^\"]*)\"");
                if (match.Success)
                {
                    string extractedFileName = match.Groups[1].Value;

                    if (extractedFileName.TrimEnd().Equals(searchName.TrimEnd(), StringComparison.OrdinalIgnoreCase))
                    {
                        Logger.LogMessage($"[FindActualFileName] Found match: '{extractedFileName}'");
                        return extractedFileName;
                    }
                }
            }

            Logger.LogMessage($"[FindActualFileName] No match found for '{searchName}'");
            return null;
        }

        private string[] LoadRawDirectoryLines(FloppyPointer floppyPointer)
        {
            DateTime dirStart = DateTime.Now;
            Logger.LogMessage($"[LoadRawDirectoryLines] Running c1541 -dir");

            // Verify network path is accessible
            if (!VerifyImagePathAccessible(floppyPointer.ImagePath))
            {
                Logger.LogMessage($"[LoadRawDirectoryLines] Image path not accessible: '{floppyPointer.ImagePath}'", LogSeverity.Error);
                return new string[0];
            }

            string arguments = $"\"{floppyPointer.ImagePath}\" -dir";

            RunProcessParameters runProcessParameters = new RunProcessParameters();
            runProcessParameters.ImagePath = floppyPointer.ImagePath;
            runProcessParameters.Arguments = arguments;
            runProcessParameters.ExecutablePath = this.Configuration.StorageAdapterSettings.Vice.ExecutablePath;
            runProcessParameters.LockType = LockType.Read;
            runProcessParameters.LockTimeoutSeconds = this.Configuration.StorageAdapterSettings.LockTimeoutSeconds;

            RunProcessResult runProcessResult = this.ProcessRunner.RunProcess(runProcessParameters);

            Logger.LogMessage($"[LoadRawDirectoryLines] c1541 -dir took {(DateTime.Now - dirStart).TotalMilliseconds}ms");

            if (runProcessResult.HasError)
            {
                Logger.LogMessage($"[LoadRawDirectoryLines] error: {runProcessResult.Error}", LogSeverity.Error);
                return new string[0];
            }

            string[] rawLines = runProcessResult.Output
                  .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            if (this.Configuration.StorageAdapterSettings.Vice.Version.StartsWith("3."))
            {
                var filteredLines = rawLines
                    .Skip(3)
                    .Take(rawLines.Length - 4)
                    .Select(line => line.Trim())
                    .ToList();
                return filteredLines.ToArray();
            }

            return rawLines;
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

        CreateFloppyResponse IStorageAdapter.CreateFloppyImage(CreateFloppyRequest createFloppyRequest)
        {
            try
            {
                // Parse inputs / defaults
                string diskName = (createFloppyRequest?.Filename ?? "NEW_DISK").Trim();
                if (string.IsNullOrWhiteSpace(diskName)) diskName = "NEW_DISK";
                string imageType = (createFloppyRequest?.MediaType ?? "D64").ToUpperInvariant();
                string label = createFloppyRequest?.InternalName ?? diskName;

                // Map supported image types -> extension + track count (used for c1541 format)
                string ext;
                int formatTracks;
                switch (imageType)
                {
                    case "D81":
                        ext = ".d81";
                        formatTracks = 80; // best-effort for 1581-style images
                        break;
                    case "G64":
                        // G64 formatting via c1541 is not supported here
                        return new CreateFloppyResponse
                        {
                            Success = false,
                            FloppyInfo = null,
                            ErrorMessage = "G64 format not supported by this implementation. Use D64 or D81."
                        };
                    default:
                        ext = ".d64";
                        formatTracks = 35; // standard 35-track D64
                        break;
                }

                string baseDir = this.Configuration.StorageAdapterSettings.NewFloppyPath;
                if (!Directory.Exists(baseDir))
                    Directory.CreateDirectory(baseDir);

                // Make a safe filename
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

                string c1541Exe = this.Configuration.StorageAdapterSettings?.Vice?.ExecutablePath;
                if (string.IsNullOrWhiteSpace(c1541Exe) || !File.Exists(c1541Exe))
                {
                    Logger.LogMessage("[CreateFloppyImage] c1541 executable not configured or not found", LogSeverity.Error);
                    return new CreateFloppyResponse
                    {
                        Success = false,
                        FloppyInfo = null,
                        ErrorMessage = "c1541 executable not configured or not found"
                    };
                }

                string diskId = "00";
                string safeLabel = label.Length > 16 ? label.Substring(0, 16) : label;

                // formatArgs: -format "LABEL,00" d64 "fullPath" -quit
                string formatArgs = $"-format \"{safeLabel},{diskId}\" d64 \"{fullPath}\" -quit";

                var formatParams = new RunProcessParameters
                {
                    ExecutablePath = c1541Exe,
                    ImagePath = fullPath,
                    Arguments = formatArgs,
                    LockType = LockType.Write,
                    LockTimeoutSeconds = this.Configuration.StorageAdapterSettings?.LockTimeoutSeconds ?? 30
                };

                Logger.LogMessage($"[CreateFloppyImage] Running c1541 to format image: {formatArgs}", LogSeverity.Verbose);
                RunProcessResult runProcessResult = this.ProcessRunner.RunProcess(formatParams);

                if (runProcessResult == null)
                {
                    Logger.LogMessage("[CreateFloppyImage] c1541 did not run (lock timeout or runner returned null)", LogSeverity.Error);
                    return new CreateFloppyResponse
                    {
                        Success = false,
                        FloppyInfo = null,
                        ErrorMessage = "c1541 did not run (lock timeout or runner error)"
                    };
                }

                Logger.LogMessage($"[CreateFloppyImage] c1541 output: {runProcessResult.Output}", LogSeverity.Verbose);
                if (runProcessResult.HasError)
                {
                    Logger.LogMessage($"[CreateFloppyImage] c1541 error: {runProcessResult.Error}", LogSeverity.Error);
                    return new CreateFloppyResponse
                    {
                        Success = false,
                        FloppyInfo = null,
                        ErrorMessage = $"c1541 format failed: {runProcessResult.Error ?? runProcessResult.Output}"
                    };
                }

                // Verify that the image exists
                if (!File.Exists(fullPath))
                {
                    Logger.LogMessage($"[CreateFloppyImage] Image file not found after c1541: {fullPath}", LogSeverity.Error);
                    return new CreateFloppyResponse
                    {
                        Success = false,
                        FloppyInfo = null,
                        ErrorMessage = "c1541 did not create the image file"
                    };
                }

                // Build FloppyInfo for resolver (resolver will set ID when inserted)
                FloppyInfo floppyInfo = new FloppyInfo();
                floppyInfo.IdLo = 0;
                floppyInfo.IdHi = 0;
                string displayName = Path.GetFileName(fullPath);
                if (displayName.Length > 64) displayName = displayName.Substring(0, 64);
                floppyInfo.ImageNameLength = (byte)displayName.Length;
                floppyInfo.ImageName = new char[64];
                displayName.ToUpperInvariant().ToCharArray().CopyTo(floppyInfo.ImageName, 0);

                // Success
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
