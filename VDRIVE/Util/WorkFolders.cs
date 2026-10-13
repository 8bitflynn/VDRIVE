using System.Text.RegularExpressions;
using VDRIVE_Contracts.Interfaces;

namespace VDRIVE.Util
{
    /// <summary>
    /// Where a session's files go: downloads from Commodore.Software / C64, SIDs converted by psid64, programs
    /// extracted from disks, dir.prg... Each session has its own folder, VDRIVE_Scratch\sessions\&lt;sessionId&gt;\,
    /// so two people can never get each other's files or catch a file halfway through being written.
    /// The folder lives as long as the session: SessionProvider deletes it when the session expires, and leftovers
    /// from the last run are deleted at startup. cs_category_roots.txt (a cache for every session) stays in
    /// VDRIVE_Scratch itself and is never touched.
    /// </summary>
    public static class WorkFolders
    {
        private const string SessionsFolder = "sessions";

        public static string Root(IConfiguration configuration)
        {
            string tempPath = string.IsNullOrWhiteSpace(configuration.TempPath) ? Path.GetTempPath() : configuration.TempPath;
            string tempFolder = string.IsNullOrWhiteSpace(configuration.TempFolder) ? "VDRIVE_Scratch" : configuration.TempFolder;
            return Path.GetFullPath(Path.Combine(tempPath, tempFolder));
        }

        public static string ForSession(IConfiguration configuration, ushort sessionId)
        {
            return Path.Combine(Root(configuration), SessionsFolder, sessionId.ToString());
        }

        /// <summary>
        /// The folder to work in, created if needed: the session's folder, or (no session: unit tests, the TCP
        /// server) one per thread as before.
        /// </summary>
        public static string Ensure(IConfiguration configuration, string workFolder)
        {
            string folder = string.IsNullOrWhiteSpace(workFolder)
                ? Path.Combine(Root(configuration), Thread.CurrentThread.ManagedThreadId.ToString())
                : workFolder;
            Directory.CreateDirectory(folder);
            return folder;
        }

        public static void DeleteSession(IConfiguration configuration, ushort sessionId, ILogger logger)
        {
            DeleteFolder(configuration, ForSession(configuration, sessionId), logger);
        }

        /// <summary>
        /// At startup: no session survives a restart, so their folders go, and so do the per-thread folders
        /// (VDRIVE_Scratch\13\...) from before session folders. Only those: anything else in VDRIVE_Scratch stays.
        /// </summary>
        public static void CleanupAtStartup(IConfiguration configuration, ILogger logger)
        {
            try
            {
                string root = Root(configuration);
                if (!Directory.Exists(root))
                {
                    return;
                }

                string sessions = Path.Combine(root, SessionsFolder);
                if (Directory.Exists(sessions))
                {
                    foreach (string folder in Directory.GetDirectories(sessions))
                    {
                        DeleteFolder(configuration, folder, logger);
                    }
                }

                foreach (string folder in Directory.GetDirectories(root))
                {
                    if (Regex.IsMatch(Path.GetFileName(folder), @"^\d+$"))     // an old per-thread folder
                    {
                        DeleteFolder(configuration, folder, logger);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogMessage($"Scratch cleanup at startup failed: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Warning);
            }
        }

        // deletes a folder only when it is inside VDRIVE_Scratch (never VDRIVE_Scratch itself or anything outside it)
        private static void DeleteFolder(IConfiguration configuration, string folder, ILogger logger)
        {
            try
            {
                string root = Root(configuration).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || full.Length <= root.Length)
                {
                    logger.LogMessage($"Not deleting {full}: not inside {root}", VDRIVE_Contracts.Enums.LogSeverity.Warning);
                    return;
                }
                if (Directory.Exists(full))
                {
                    Directory.Delete(full, true);
                    logger.LogMessage($"Removed scratch folder {full}");
                }
            }
            catch (Exception ex)
            {
                logger.LogMessage($"Could not remove scratch folder {folder}: {ex.Message}", VDRIVE_Contracts.Enums.LogSeverity.Warning);
            }
        }
    }
}
