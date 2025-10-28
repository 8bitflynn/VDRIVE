using System.Collections.Concurrent;
using System.Diagnostics;
using VDRIVE_Contracts.Enums;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace VDRIVE.Drive
{
    public abstract class StorageAdapterBase
    {
        protected IConfiguration Configuration;
        protected ILogger Logger;

        protected byte[] BuildDirectoryPrg(string[] rawLines, string diskName = "")
        {
            // HACK: c1541.exe outputs "Empty image" when image is empty
            if (rawLines.Length == 3 && rawLines[1].StartsWith("Empty image"))
            {
                rawLines[1] = $"0 \"Empty image\" ";
            }

            const ushort LOAD_ADDR = 0x0801;
            const int FULL_LENGTH = 256 + 2;    // 256‐byte block + 2‐byte header

            var prg = new List<byte>();
            ushort ramPtr = LOAD_ADDR;

            // 1) PRG header: load address
            prg.Add(LOAD_ADDR & 0xFF);
            prg.Add(LOAD_ADDR >> 8);

            // 2) Build each line, always patching its “next” pointer
            for (int i = 0; i < rawLines.Length; i++)
            {
                bool isHeader = i == 0;
                bool isTrailer = i == rawLines.Length - 1;
                string raw = rawLines[i].Trim();

                // [ptrLo][ptrHi][line#Lo][line#Hi][…content…][0x00]
                var line = new List<byte> { 0x00, 0x00 };

                // parse block‐count as BASIC line number
                int spaceIndex = raw.IndexOf(' ');
                if (!int.TryParse(
                        spaceIndex > 0 ? raw.Substring(0, spaceIndex) : "0",
                        out int blockSize))
                    blockSize = 0;
                line.Add((byte)(blockSize & 0xFF));
                line.Add((byte)(blockSize >> 8));

                if (!isHeader && !isTrailer)
                {
                    string blockSizeAsString = blockSize.ToString();
                    int neededSpaces = 4 - blockSizeAsString.Length;
                    for (int sp = 0; sp < neededSpaces; sp++)
                    {
                        line.Add((byte)' ');
                    }
                }

                // grab everything after the count
                string content = spaceIndex > 0
                    ? raw.Substring(spaceIndex + 1)
                    : raw;

                // always strip only real spaces, then re-insert two-space indent
                if (!isHeader && !isTrailer)
                {
                    content = content.TrimStart();

                    var quoteSplit = content.Split('\"');
                    content = $"\"{quoteSplit[1]}\"";

                    int neededSpaces = 19 - content.Length;
                    for (int sp = 0; sp < neededSpaces; sp++)
                    {
                        content += " ";
                    }
                    content += quoteSplit[2]?.Trim();
                    neededSpaces = 32 - line.Count - content.Length - 1;

                    for (int sp = 0; sp < neededSpaces; sp++)
                    {
                        content += " ";
                    }
                }

                if (isHeader)
                {
                    content = content.TrimStart();

                    var spaceSplit = content.Split('\"');
                    content = $"\"{spaceSplit[0]}{spaceSplit[1]}\"{spaceSplit[2]}";

                    // inverse-on for header
                    line.Add(0x12);
                }

                if (isTrailer)
                {
                    int neededSpaces = 30 - line.Count - content.Length - 1;

                    for (int sp = 0; sp < neededSpaces; sp++)
                    {
                        content += " ";
                    }
                }

                // append the text in PETSCII (uppercase)
                // foreach (char c in content.ToUpperInvariant())
                foreach (char c in content.ToUpperInvariant())
                {
                    //byte b = this.AsciiToPetscii(c);
                    byte b = (byte)c;
                    line.Add(b);
                }

                // end-of-line marker
                line.Add(0x00);

                // patch this line’s two-byte pointer → nextAddr
                ushort nextAddr = (ushort)(ramPtr + line.Count);
                line[0] = (byte)(nextAddr & 0xFF);
                line[1] = (byte)(nextAddr >> 8);
                ramPtr = nextAddr;

                prg.AddRange(line);
            }

            // 3) Terminator record: two zero bytes at ramPtr
            prg.Add(0x00);
            prg.Add(0x00);

            // 4) Pad to full 258 bytes
            while (prg.Count < FULL_LENGTH)
                prg.Add(0x00);

            return prg.ToArray();
        }

        protected LoadResponse BuildLoadResponse(LoadRequest loadRequest, byte[] payload, byte responseCode)
        {
            LoadResponse loadResponse = new LoadResponse();
            loadResponse.ResponseCode = responseCode;

            if (payload != null)
            {
                loadResponse.SyncByte = (byte)'+';

                // send binary length in 24 bits
                int lengthMinusMemoryPtr = payload.Length - 2;
                loadResponse.ByteCountLo = (byte)(lengthMinusMemoryPtr & 0xFF); // LSB
                loadResponse.ByteCountMid = (byte)((lengthMinusMemoryPtr >> 8) & 0xFF);
                loadResponse.ByteCountHi = (byte)((lengthMinusMemoryPtr >> 16) & 0xFF); // MSB

                byte loChunkLength = (byte)this.Configuration.ChunkSize;
                byte hiChunkLength = (byte)(this.Configuration.ChunkSize >> 8);
                loadResponse.ChunkSizeLo = loChunkLength;
                loadResponse.ChunkSizeHi = hiChunkLength;

                int memoryLocation = (payload[1] << 8) + payload[0];

                byte loDestPtr = payload[0];
                byte hiDestPtr = payload[1];

                loadResponse.DestPtrLo = loDestPtr;
                loadResponse.DestPtrHi = hiDestPtr;
            }

            return loadResponse;
        }

        private static readonly ConcurrentDictionary<string, ReaderWriterLockSlim> ImageLocks = new();

        protected virtual RunProcessResult RunProcessWithLock(RunProcessParameters runProcessParameters, bool isWrite)
        {
            if (runProcessParameters == null || string.IsNullOrWhiteSpace(runProcessParameters.ImagePath))
            {
                return null;
            }

            Logger.LogMessage($"{(isWrite ? "Write" : "Read")} lock acquired for {runProcessParameters.ImagePath}");

            var lockSlim = GetImageLock(runProcessParameters.ImagePath);

            if (isWrite)
                lockSlim.EnterWriteLock();
            else
                lockSlim.EnterReadLock();

            try
            {
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
                        Error = process.StandardError.ReadToEnd()
                    };
                    process.WaitForExit();

                    if (!string.IsNullOrWhiteSpace(runProcessResult.Output))
                        Logger.LogMessage(runProcessResult.Output.Trim());
                    if (!string.IsNullOrWhiteSpace(runProcessResult.Error))
                        Logger.LogMessage(runProcessResult.Error.Trim(), LogSeverity.Error);

                    return runProcessResult;
                }
            }
            finally
            {
                if (isWrite)
                    lockSlim.ExitWriteLock();
                else
                    lockSlim.ExitReadLock();

                Logger.LogMessage($"{(isWrite ? "Write" : "Read")} lock release for {runProcessParameters.ImagePath}");
            }
        }

        private ReaderWriterLockSlim GetImageLock(string imagePath)
        {            
            return ImageLocks.GetOrAdd(imagePath, _ => new ReaderWriterLockSlim());
        }
    }
}
