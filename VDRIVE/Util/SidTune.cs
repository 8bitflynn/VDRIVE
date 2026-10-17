namespace VDRIVE.Util
{
    // A SID tune for a C64 program's own player (the SAM's Place jukebox): the tune without psid64's driver, and
    // its numbers in a small header, from the PSID/RSID file VDRIVE downloaded on mount (raw.sid).
    //   LOAD "*,H" -> the header: 12 bytes, stamped for $CF70 (or anywhere with LOAD "*,H",8 and X/Y)
    //   LOAD "*,N" -> the tune at its own load address (what "psid64 -n" makes)
    // Header: load lo/hi, end lo/hi (last byte), init lo/hi, play lo/hi, songs, start song (1 = first),
    //         flags (bit 0: no play routine - the tune runs its own interrupt; bit 1: the start song is timed by a
    //         CIA timer, not 50 times a second; bit 2: RSID - needs a real C64 environment), 0 (reserved)
    public class SidTune
    {
        public const ushort HeaderAddress = 0xCF70;     // between the VDRIVE client ($C000-$CF6E) and the jukebox player ($CF80)

        public ushort LoadAddress { get; private set; }
        public ushort InitAddress { get; private set; }
        public ushort PlayAddress { get; private set; }
        public byte Songs { get; private set; }
        public byte StartSong { get; private set; }
        public byte Flags { get; private set; }
        public byte[] Data { get; private set; }

        public static SidTune Parse(byte[] sid)
        {
            if (sid == null || sid.Length < 0x7C) return null;
            string magic = System.Text.Encoding.ASCII.GetString(sid, 0, 4);
            if (magic != "PSID" && magic != "RSID") return null;

            int dataOffset = Word(sid, 6);
            ushort load = (ushort)Word(sid, 8);
            int start = dataOffset;
            if (load == 0)                                              // the load address is the data's first 2 bytes
            {
                if (sid.Length < dataOffset + 2) return null;
                load = (ushort)(sid[dataOffset] | (sid[dataOffset + 1] << 8));
                start += 2;
            }
            int length = sid.Length - start;
            if (length <= 0 || load + length > 0x10000) return null;

            ushort init = (ushort)Word(sid, 0x0A);
            ushort play = (ushort)Word(sid, 0x0C);
            int songs = Math.Max(1, Word(sid, 0x0E));
            int startSong = Math.Clamp(Word(sid, 0x10), 1, songs);
            uint speed = (uint)((sid[0x12] << 24) | (sid[0x13] << 16) | (sid[0x14] << 8) | sid[0x15]);

            byte flags = 0;
            if (play == 0) flags |= 1;
            if (((speed >> Math.Min(startSong - 1, 31)) & 1) != 0) flags |= 2;
            if (magic == "RSID") flags |= 4;

            byte[] data = new byte[length];
            Array.Copy(sid, start, data, 0, length);
            return new SidTune
            {
                LoadAddress = load,
                InitAddress = init == 0 ? load : init,                  // 0 = init at the load address
                PlayAddress = play,
                Songs = (byte)Math.Min(songs, 255),
                StartSong = (byte)startSong,
                Flags = flags,
                Data = data
            };
        }

        // the header as a PRG for $CF70
        public byte[] HeaderPrg()
        {
            ushort end = (ushort)(LoadAddress + Data.Length - 1);
            return new byte[]
            {
                (byte)(HeaderAddress & 0xFF), (byte)(HeaderAddress >> 8),
                (byte)(LoadAddress & 0xFF), (byte)(LoadAddress >> 8),
                (byte)(end & 0xFF), (byte)(end >> 8),
                (byte)(InitAddress & 0xFF), (byte)(InitAddress >> 8),
                (byte)(PlayAddress & 0xFF), (byte)(PlayAddress >> 8),
                Songs, StartSong, Flags, 0
            };
        }

        // the tune alone as a PRG at its own address
        public byte[] TunePrg()
        {
            byte[] prg = new byte[Data.Length + 2];
            prg[0] = (byte)(LoadAddress & 0xFF);
            prg[1] = (byte)(LoadAddress >> 8);
            Array.Copy(Data, 0, prg, 2, Data.Length);
            return prg;
        }

        // "*,H" / "*,N" (any name before the comma: the mounted tune is meant)
        public static bool TryGetPart(string fileName, out bool header)
        {
            header = false;
            if (string.IsNullOrEmpty(fileName)) return false;
            int comma = fileName.LastIndexOf(',');
            if (comma < 0) return false;
            string sw = fileName.Substring(comma + 1).Trim();
            if (sw.Equals("H", StringComparison.OrdinalIgnoreCase)) { header = true; return true; }
            return sw.Equals("N", StringComparison.OrdinalIgnoreCase);
        }

        private static int Word(byte[] b, int at) => (b[at] << 8) | b[at + 1];     // PSID header words are big-endian
    }
}
