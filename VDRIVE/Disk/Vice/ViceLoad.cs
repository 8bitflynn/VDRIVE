using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Disk.Vice
{
    public class ViceLoad : ILoad
    {
        public ViceLoad(IConfiguration configuration, ILog logger)
        {
            this.Configuration = configuration;
            this.Logger = logger;          
        }
        private IConfiguration Configuration;
        private ILog Logger;

        LoadResponse ILoad.Load(LoadRequest loadRequest, IFloppyResolver floppyResolver, out byte[] payload)
        {
            try
            {
                byte responseCode = 0xff;
                string filename = new string(loadRequest.FileName.TakeWhile(c => c != '\0').ToArray());

                // hack to allow loading of PRG files directly for now 
                // need to instead create a temp D64 wrapper disk first and then treat it as a normal file load
                // but this will work for now
                FloppyPointer? floppyPointer = floppyResolver.GetInsertedFloppyPointer().Value;

                if (floppyPointer.Value.ImagePath.ToLower().EndsWith(".prg"))
                {
                    payload = File.ReadAllBytes(floppyResolver.GetInsertedFloppyPointer().Value.ImagePath);

                    // build a temp D64 image with just this file in it
                    //string tempImagePath = this.BuildImageForPRG(filename, Path.Combine(this.Configuration.TempPath, "tempdisk.d64"));

                    //FloppyPointer? currentFloppyPointer = floppyResolver.GetInsertedFloppyPointer().Value;
                    //FloppyPointer newFloppyPointer = new FloppyPointer
                    //{
                    //    Id = currentFloppyPointer.Value.Id,
                    //    ImagePath = tempImagePath
                    //};
                    //floppyResolver.SetInsertedFloppyPointer(newFloppyPointer); // set the floppy pointer to newly created image
                }
                else if (filename.StartsWith("$")) // TODO: implement wildcards / filtering
                {
                    payload = LoadDirectory(loadRequest, floppyResolver.GetInsertedFloppyInfo().Value, floppyResolver.GetInsertedFloppyPointer().Value);
                }
                else if (filename.StartsWith("*"))
                {
                    string[] rawLines = LoadRawDirectoryLines(floppyResolver.GetInsertedFloppyPointer().Value);

                    string lineWithFirstFile = rawLines[1];

                    // Match anything inside double quotes, including spaces
                    Match match = Regex.Match(lineWithFirstFile, "\"([^\"]*)\"");
                    if (match.Success)
                    {
                        string extracted = match.Groups[1].Value;

                        string[] tokens = lineWithFirstFile.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        loadRequest.FileName = extracted.ToCharArray();
                        loadRequest.FileNameLength = (byte)extracted.Length;
                        payload = LoadFile(loadRequest, floppyResolver.GetInsertedFloppyInfo().Value, floppyResolver.GetInsertedFloppyPointer().Value, out responseCode);
                    }
                    else
                    {
                        payload = null;
                    }
                }
                else
                {
                    payload = LoadFile(loadRequest, floppyResolver.GetInsertedFloppyInfo().Value, floppyResolver.GetInsertedFloppyPointer().Value, out responseCode);
                }

                LoadResponse loadResponse = BuildLoadResponse(loadRequest, payload, responseCode);
                return loadResponse;
            }
            catch (Exception exception)
            {
                this.Logger.LogMessage(@"ERROR: " + exception.Message);

                payload = null;
                return BuildLoadResponse(loadRequest, payload, 0x04); 
            }
        }

        protected LoadResponse BuildLoadResponse(LoadRequest loadRequest, byte[] payload, byte responseCode, int chunkSize = 1024)
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

                byte loChunkLength = (byte)chunkSize;
                byte hiChunkLength = (byte)(chunkSize >> 8);
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
            if (!Directory.Exists(this.Configuration.TempPath))
                Directory.CreateDirectory(this.Configuration.TempPath);

            string safeName = new string(loadRequest.FileName.TakeWhile(c => c != '\0').ToArray()).ToLowerInvariant();

            string outPrgPath = Path.Combine(this.Configuration.TempPath, safeName).Trim();

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
            // TODO: PRG does not need to be written to disk, just return byte array
            // but its great for debugging so need to make it configurable
            string dirPrgPath = Path.Combine(this.Configuration.TempPath, "dir.prg");

            // Ensure temp directory exists
            if (!Directory.Exists(this.Configuration.TempPath))
            {
                Directory.CreateDirectory(this.Configuration.TempPath);
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


        public string BuildImage(string outputPathToImage, string imageType)
        {
            //c1541 - format "DISKNAME",id "C:\path\to\newimage.d64" - write "C:\path\to\program.prg" @8:PROGRAM - quit

            // string imagePath = Path.Combine(this.Configuration.TempPath, "tempdisk.d64");

            // call c1541 to get text directory listing
            var psi = new ProcessStartInfo
            {
                FileName = this.Configuration.C1541Path,
                Arguments = $"\"format \"TEMPDISK\" {outputPathToImage} -quit",
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

            return null;
        }

        public bool AddFileToImage(string imagePath, string filePathInImage, byte[] fileData, bool overwrite)
        {
            throw new NotImplementedException();
        }

        private string BuildImageForPRG(string prgPath, string imagePath)
        {
            return Path.Combine(imagePath, prgPath);
        }
    }
}
