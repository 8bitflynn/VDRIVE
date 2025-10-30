using System.Collections.Concurrent;
using System.Diagnostics;
using VDRIVE_Contracts.Enums;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Util
{
    public class LockingProcessRunner : IProcessRunner
    {
        public LockingProcessRunner(IConfiguration configurtion, ILogger logger)
        {
            this.Configuration = configurtion;
            this.Logger = logger;
        }
        private readonly IConfiguration Configuration;
        private readonly ILogger Logger;

        private static readonly ConcurrentDictionary<string, ReaderWriterLockSlim> ImageLocks = new();   

        public RunProcessResult RunProcessWithLock(RunProcessParameters runProcessParameters)
        {
            if (runProcessParameters == null || string.IsNullOrWhiteSpace(runProcessParameters.ImagePath))
            {
                return null;
            }

            ReaderWriterLockSlim readerWriterLockSlim = null;
            if (runProcessParameters.LockType != LockType.None)
            {
                Logger.LogMessage($"{runProcessParameters.LockType.ToString()} lock acquired for {runProcessParameters.ImagePath}");

                readerWriterLockSlim = GetImageLock(runProcessParameters.ImagePath);

                if (runProcessParameters.LockType == LockType.Write)
                    readerWriterLockSlim.EnterWriteLock();
                else
                    readerWriterLockSlim.EnterReadLock();
            }

            try
            {
                this.Logger.LogMessage($"Running process {runProcessParameters.ExecutablePath} {runProcessParameters.Arguments}");

                var psi = new ProcessStartInfo
                {
                    FileName = runProcessParameters.ExecutablePath,
                    Arguments = runProcessParameters.Arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(psi))
                {
                    RunProcessResult runProcessResult = new RunProcessResult
                    {
                        Output = process.StandardOutput.ReadToEnd(),
                        Error = process.StandardError.ReadToEnd(),
                    };
                    process.WaitForExit();

                    if (!string.IsNullOrWhiteSpace(runProcessResult.Output))
                        Logger.LogMessage(runProcessResult.Output.Trim());
                    if (!string.IsNullOrWhiteSpace(runProcessResult.Error))
                        Logger.LogMessage(runProcessResult.Error.Trim(), LogSeverity.Error);

                    this.Logger.LogMessage($"Process completed {runProcessParameters.ExecutablePath} {runProcessParameters.Arguments}");


                    return runProcessResult;
                }
            }
            finally
            {
                if (runProcessParameters.LockType != LockType.None)
                {
                    if (runProcessParameters.LockType == LockType.Write)
                        readerWriterLockSlim.ExitWriteLock();
                    else
                        readerWriterLockSlim.ExitReadLock();

                    Logger.LogMessage($"{(runProcessParameters.LockType == LockType.Write ? "Write" : "Read")} lock released for {runProcessParameters.ImagePath}");
                }
            }
        }

        private ReaderWriterLockSlim GetImageLock(string imagePath)
        {
            return ImageLocks.GetOrAdd(imagePath, _ => new ReaderWriterLockSlim());
        }
    }
}
