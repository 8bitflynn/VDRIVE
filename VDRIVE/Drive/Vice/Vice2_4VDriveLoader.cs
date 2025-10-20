using System.Diagnostics;
using System.Text.RegularExpressions;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Drive.Vice
{
    public class Vice2_4VDriveLoader : IVDriveLoader
    {
        public Vice2_4VDriveLoader(IConfiguration configuration, IVDriveLoggger logger)
        {
            this.Configuration = configuration;
            this.Logger = logger;          
        }
        private IConfiguration Configuration;
        private IVDriveLoggger Logger;

        LoadResponse IVDriveLoader.Load(LoadRequest loadRequest, IFloppyResolver floppyResolver, out byte[] payload)
        {
            try
            {
                byte responseCode = 0xff; // success
                string filename = new string(loadRequest.FileName.TakeWhile(c => c != '\0').ToArray());
                
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
                this.Logger.LogMessage(@"ERROR: " + exception.Message);

                payload = null;
                return BuildLoadResponse(loadRequest, payload, 0x04); // TODO: map to "device not found" to match stock c64 behavior
            }
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

        protected byte[] LoadFile(LoadRequest loadRequest, FloppyInfo floppyInfo, FloppyPointer floppyPointer, out byte responseCode)
        {
            string fullPath = Path.Combine(this.Configuration.TempPath, this.Configuration.TempFolder);
            if (!Directory.Exists(fullPath));
                Directory.CreateDirectory(fullPath);

            string safeName = new string(loadRequest.FileName.TakeWhile(c => c != '\0').ToArray()).ToLowerInvariant();
            string outPrgPath = Path.Combine(this.Configuration.TempPath, this.Configuration.TempFolder, safeName).Trim();
            outPrgPath = outPrgPath.Replace(@"/", "_");
            if (File.Exists(outPrgPath))
                File.Delete(outPrgPath);

            string fileSpec = $"@8:{safeName}";     

            // execute c1541 to extract the file
            var psi = new ProcessStartInfo
            {
                FileName = this.Configuration.C1541Path,
                Arguments = $"\"{new string (floppyPointer.ImagePath)}\" -read \"{fileSpec}\" \"{outPrgPath}\" -quit",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var proc = Process.Start(psi))
            {
                string c1541Out = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                this.Logger.LogMessage(c1541Out.Trim());

                if (!c1541Out.StartsWith("Reading file"))
                {
                    // TODO: map real codes from c1541.exe as they appear to be different?
                    responseCode = 0x04; // file not found
                    return null;
                }
            }

            if (File.Exists(outPrgPath))
            {
                this.Logger.LogMessage($"File extracted: {outPrgPath}");
                responseCode = 0xff; // success
                return File.ReadAllBytes(outPrgPath);
            }
            else
            {
                this.Logger.LogMessage($"ERROR: {safeName}.prg not found in temp directory.");
                responseCode = 0x04; // file not found
                return null;
            }
        }     

        protected byte[] LoadDirectory(LoadRequest loadRequest, FloppyInfo floppyInfo, FloppyPointer floppyPointer)
        {
            string fullPath = Path.Combine(this.Configuration.TempPath, this.Configuration.TempFolder);
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
            byte[] dirPrgBytes = this.BuildDirectoryPrg(rawLines);

            File.WriteAllBytes(dirPrgPath, dirPrgBytes);

            if (dirPrgBytes != null && dirPrgBytes.Length > 0)
            {
                this.Logger.LogMessage($"$ created successfully: {dirPrgPath}");
                return dirPrgBytes;
            }

            return null;
        }

        private string[] LoadRawDirectoryLines(FloppyPointer floppyPointer)
        {
            // call c1541 to get text directory listing
            var psi = new ProcessStartInfo
            {
                FileName = this.Configuration.C1541Path,
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

        private byte AsciiToPetscii(char c)
        {
            if (c >= 'A' && c <= 'Z') return (byte)(c);           // PETSCII uppercase = ASCII uppercase
            if (c >= 'a' && c <= 'z') return (byte)(c - 0x20 + 0x80); // PETSCII lowercase = ASCII lowercase + 0x40
            return (byte)c; // fallback for digits and punctuation
        }       
    }
}
