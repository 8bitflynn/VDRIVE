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
            string outPrgPath = Path.Combine(fullPath, safeName.Replace(@"/", "_"));
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
            
            // Check for network errors
            if (runProcessResult.HasError && runProcessResult.Error?.Contains("network", StringComparison.OrdinalIgnoreCase) == true)
            {
                Logger.LogMessage($"[LoadRawDirectoryLines] Network error: {runProcessResult.Error}", LogSeverity.Error);
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
    }
}
