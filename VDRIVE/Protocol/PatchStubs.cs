namespace VDRIVE.Protocol
{
    // Patch stubs: small C64 programs the server sends instead of a file, picked by a switch at the end
    // of the name. VDRIVE loads a stub (load status 0xFE = run after load) and the stub does the rest.
    //
    //   LOAD"NAME,P",8   run NAME even when it fills all of memory (over VDRIVE at $C000 and the RAM
    //                    under I/O): the stub asks for "NAME,P2" and writes the bytes itself
    //
    // Source of the stubs: stubs/patch_run.asm (ACME). Rebuild the byte arrays here when it changes.
    public static class PatchStubs
    {
        public const byte RunAfterLoad = 0xFE;     // load status: VDRIVE jumps to the load address
        public const string RunSwitch = ",P";      // LOAD"NAME,P",8 -> the stub
        public const string RunDataSwitch = ",P2"; // the stub's own request -> the segment stream

        // patch_run.asm, assembled at $0100 (the bottom of the stack page)
        private const ushort RunStubAddress = 0x0100;
        private const int RunStubNameLengthOffset = 0x97;   // fname_len; the name follows it
        private const ushort RunStubRunBasic = 0x015D;      // run_basic: CLR + RUN
        private const int RunStubMaxEnd = 0x01C0;           // the stack lives above this
        private static readonly byte[] RunStub =
        {
            0xA2, 0xFF, 0x9A, 0xA9, 0x00, 0x85, 0x9D, 0xAD, 0x97, 0x01, 0xA2, 0x98, 0xA0, 0x01, 0x20, 0xBD,
            0xFF, 0x20, 0x1B, 0xC0, 0xB0, 0x4D, 0x78, 0x20, 0x6D, 0x01, 0x85, 0xFB, 0x20, 0x6D, 0x01, 0x85,
            0xFC, 0x20, 0x6D, 0x01, 0x85, 0xFD, 0x20, 0x6D, 0x01, 0x85, 0xFE, 0x05, 0xFD, 0xF0, 0x1E, 0xA0,
            0x00, 0x20, 0x6D, 0x01, 0xA2, 0x34, 0x86, 0x01, 0x91, 0xFB, 0xA2, 0x37, 0x86, 0x01, 0xC8, 0xD0,
            0x02, 0xE6, 0xFC, 0xE6, 0xFD, 0xD0, 0xEA, 0xE6, 0xFE, 0xD0, 0xE6, 0xF0, 0xCA, 0xA9, 0x00, 0x8D,
            0x03, 0xDD, 0xAD, 0x0D, 0xDD, 0xA9, 0x80, 0x85, 0x9D, 0x58, 0x6C, 0xFB, 0x00, 0x20, 0x59, 0xA6,
            0x4C, 0xAE, 0xA7, 0xA9, 0x80, 0x85, 0x9D, 0x58, 0xA2, 0x04, 0x6C, 0x00, 0x03, 0xA2, 0x0A, 0x86,
            0x9F, 0xA2, 0x00, 0x86, 0x9E, 0xA9, 0x10, 0x2C, 0x0D, 0xDD, 0xD0, 0x17, 0xCA, 0xD0, 0xF6, 0xC6,
            0x9E, 0xD0, 0xF2, 0xC6, 0x9F, 0xD0, 0xEE, 0xA9, 0x00, 0x8D, 0x03, 0xDD, 0xA9, 0x37, 0x85, 0x01,
            0x6C, 0xFC, 0xFF, 0xAD, 0x01, 0xDD, 0x60, 0x00,
        };

        // where VDRIVE lives on the C64: when a program covers it, LOAD/SAVE go back to the KERNAL's
        private const int VdriveStart = 0xC000, VdriveEnd = 0xCFFF;
        private const ushort BasicStart = 0x0801;
        private const ushort Ready = 0xA474;

        // true when a program (a PRG, load address first) would land on VDRIVE itself
        public static bool CoversVdrive(byte[] prg)
        {
            if (prg == null || prg.Length < 3) return false;
            int start = prg[0] | (prg[1] << 8);
            int end = start + prg.Length - 3;
            return start <= VdriveEnd && end >= VdriveStart;
        }

        // "NAME,P" -> "NAME" (null when the name has no run switch)
        public static string GetRunName(string fileName)
        {
            if (fileName == null || fileName.Length <= RunSwitch.Length) return null;
            if (!fileName.EndsWith(RunSwitch, StringComparison.OrdinalIgnoreCase)) return null;
            return fileName.Substring(0, fileName.Length - RunSwitch.Length);
        }

        // "NAME,P2" -> "NAME"
        public static string GetRunDataName(string fileName)
        {
            if (fileName == null || fileName.Length <= RunDataSwitch.Length) return null;
            if (!fileName.EndsWith(RunDataSwitch, StringComparison.OrdinalIgnoreCase)) return null;
            return fileName.Substring(0, fileName.Length - RunDataSwitch.Length);
        }

        // The stub as a PRG (load address first), with "NAME,P2" in it for its own request.
        // nameBytes: the name as the C64 sent it, without the switch.
        public static byte[] BuildRunStub(byte[] nameBytes)
        {
            byte[] request = nameBytes.Concat(System.Text.Encoding.ASCII.GetBytes(RunDataSwitch)).ToArray();
            if (RunStubAddress + RunStubNameLengthOffset + 1 + request.Length > RunStubMaxEnd)
                throw new ArgumentException("name too long for the stub");

            var prg = new List<byte> { (byte)(RunStubAddress & 0xFF), (byte)(RunStubAddress >> 8) };
            prg.AddRange(RunStub);
            prg[2 + RunStubNameLengthOffset] = (byte)request.Length;
            prg.AddRange(request);
            return prg.ToArray();
        }

        // What the stub receives for a program (a PRG, load address first):
        //   [addr][65536 - len][bytes] ...  [go][0000]
        // The VDRIVE load response before it still needs a 2-byte address: the program's own.
        public static byte[] BuildRunData(byte[] prg, out string error)
        {
            error = null;
            if (prg == null || prg.Length < 3) { error = "EMPTY FILE"; return null; }

            int start = prg[0] | (prg[1] << 8);
            int length = prg.Length - 2;
            int end = start + length - 1;
            if (start < 0x0200) { error = "LOADS BELOW $0200"; return null; }   // the stub and the stack
            if (end > 0xFFFF) { error = "FILE TOO BIG"; return null; }

            var data = new List<byte> { prg[0], prg[1] };   // the address VDRIVE reads first

            // VDRIVE gets overwritten: LOAD/SAVE back to the KERNAL (before the program, so its own
            // bytes there win - an autostart at $0326 for example)
            if (CoversVdrive(prg))
                AddSegment(data, 0x0330, new byte[] { 0xA5, 0xF4, 0xED, 0xF5 });

            AddSegment(data, start, prg.Skip(2).ToArray());

            // end of program + 1 in $2D/$2E, as LOAD leaves it
            int afterEnd = (end + 1) & 0xFFFF;
            AddSegment(data, 0x002D, new byte[] { (byte)(afterEnd & 0xFF), (byte)(afterEnd >> 8) });

            // then: RUN a BASIC program, else READY. (where an autostart takes over through $0302)
            int go = start == BasicStart ? RunStubRunBasic : Ready;
            data.Add((byte)(go & 0xFF));
            data.Add((byte)(go >> 8));
            data.Add(0);
            data.Add(0);
            return data.ToArray();
        }

        private static void AddSegment(List<byte> data, int address, byte[] bytes)
        {
            int count = (0x10000 - bytes.Length) & 0xFFFF;   // the stub counts up to 0
            data.Add((byte)(address & 0xFF));
            data.Add((byte)(address >> 8));
            data.Add((byte)(count & 0xFF));
            data.Add((byte)(count >> 8));
            data.AddRange(bytes);
        }
    }
}
