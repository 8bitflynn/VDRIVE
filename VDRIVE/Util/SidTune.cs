namespace VDRIVE.Util
{
    // A SID tune for a C64 program's own player (the SAM's Place jukebox): the tune without psid64's driver, and
    // its numbers in a small header, from the PSID/RSID file VDRIVE downloaded on mount (raw.sid).
    //   LOAD "*,H" -> the header: 12 bytes, stamped for $CF70 (or anywhere with LOAD "*,H",8 and X/Y)
    //   LOAD "*,N" -> the tune at its own load address (what "psid64 -n" makes)
    // Header: load lo/hi, end lo/hi (last byte), init lo/hi, play lo/hi, songs, start song (1 = first),
    //         flags (bit 0: no play routine - the tune runs its own interrupt; bit 1: the start song is timed by a
    //         CIA timer, not 50 times a second; bit 2: RSID - needs a real C64 environment), zero page: how many
    //         bytes from $02 up the tune uses (sidreloc moved them there), $FF = not moved - its own, anywhere
    public class SidTune
    {
        public const ushort HeaderAddress = 0xCF70;     // between the VDRIVE client ($C000-$CF6E) and the jukebox player ($CF80)

        public ushort LoadAddress { get; private set; }
        public ushort InitAddress { get; private set; }
        public ushort PlayAddress { get; private set; }
        public byte Songs { get; private set; }
        public byte StartSong { get; private set; }
        public byte Flags { get; private set; }
        public byte ZeroPage { get; private set; } = 0xFF;  // bytes used from ZeroPageFirst up; $FF = where it always was
        public byte[] Data { get; private set; }

        public const int ZeroPageFirst = 0x02, ZeroPageLast = 0x23;     // where sidreloc puts a tune's zero page

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

        // A tune for the jukebox, made safe to play beside a program: moved with sidreloc (Linus Akesson's, MIT) into
        // $1000-$1FFF - the player only has that room - and its zero page into $02-$23, which the player swaps in
        // around each call (a tune that used the KERNAL's own bytes - the screen's line table, BASIC's CHRGET - made a
        // mess of the screen). 4 KB at most, its own play routine (CIA-timed is fine). Done once per downloaded tune:
        // kept next to raw.sid (raw_1000.sid, raw_1000.zp). Without sidreloc, or when it can't: the tune as it was,
        // ZeroPage $FF - the jukebox doesn't play it.
        public static SidTune RelocateIfNeeded(SidTune tune, string rawSidPath, string sidRelocPath, Action<string> log)
        {
            if (tune == null || string.IsNullOrEmpty(sidRelocPath) || !File.Exists(sidRelocPath)) return tune;
            int end = tune.LoadAddress + tune.Data.Length - 1;
            if ((tune.Flags & 5) != 0 || tune.Data.Length > 0x1000) return tune;             // its own interrupt / RSID, or too big

            string folder = Path.GetDirectoryName(rawSidPath) ?? "";
            string moved = Path.Combine(folder, "raw_1000.sid");
            string zpFile = Path.Combine(folder, "raw_1000.zp");
            try
            {
                if (!File.Exists(moved) || !File.Exists(zpFile) || File.GetLastWriteTimeUtc(moved) < File.GetLastWriteTimeUtc(rawSidPath))
                {
                    try { File.Delete(moved); File.Delete(zpFile); } catch { }
                    // only the songs up to the one that plays (sidreloc plays each through: Break Thru's 25 took 20 s,
                    // longer than a C64 waits for an answer; its first alone 1 s)
                    byte[] sid = File.ReadAllBytes(rawSidPath);
                    sid[0x0E] = sid[0x10];
                    sid[0x0F] = sid[0x11];
                    string input = Path.Combine(folder, "raw_songs.sid");
                    File.WriteAllBytes(input, sid);

                    var psi = new System.Diagnostics.ProcessStartInfo(sidRelocPath,
                        $"-v -p 10 -z {ZeroPageFirst:x2}-{ZeroPageLast:x2} \"{input}\" \"{moved}\"")
                    {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                    };
                    using (var p = System.Diagnostics.Process.Start(psi))
                    {
                        var errors = p.StandardError.ReadToEndAsync();      // (read alongside, so the timeout below still counts)
                        var output = p.StandardOutput.ReadToEndAsync();
                        if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } log?.Invoke("[SID] sidreloc took too long"); try { File.Delete(moved); } catch { } return tune; }
                        // (the exit code's low 5 bits are the error; $20 "wrote outside the tune" - a CIA-timed tune
                        // sets its timer - and $40 "a few bad pitches, within the tolerance" are warnings)
                        string said = errors.Result;
                        if ((p.ExitCode & 0x1F) != 0 || !File.Exists(moved))
                        {
                            string why = said.Trim().Split('\n').LastOrDefault()?.Trim();
                            log?.Invoke($"[SID] sidreloc couldn't move it (exit {p.ExitCode}): {why}");
                            try { File.Delete(moved); } catch { }
                            return tune;
                        }
                        // "New zero-page addresses:  02 03 04 05" - packed from $02 up: their number is what to swap
                        var line = said.Split('\n').FirstOrDefault(l => l.StartsWith("New zero-page addresses:"));
                        int used = line == null ? 0 : line.Substring(line.IndexOf(':') + 1).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                        File.WriteAllText(zpFile, used.ToString());
                    }
                }
                // -p 10 moves it by whole pages: $B3EE comes out at $10EE - anywhere in $1000-$1FFF will do
                SidTune relocated = Parse(File.ReadAllBytes(moved));
                int movedEnd = relocated == null ? 0 : relocated.LoadAddress + relocated.Data.Length - 1;
                if (relocated == null || relocated.LoadAddress < 0x1000 || movedEnd > 0x1FFF)
                {
                    log?.Invoke(relocated == null ? "[SID] sidreloc's result couldn't be read"
                        : $"[SID] sidreloc moved it to ${relocated.LoadAddress:X4}-${movedEnd:X4}, outside $1000-$1FFF");
                    return tune;
                }
                if (!int.TryParse(File.ReadAllText(zpFile).Trim(), out int zp) || zp > ZeroPageLast - ZeroPageFirst + 1) return tune;
                relocated.ZeroPage = (byte)zp;
                log?.Invoke($"[SID] relocated ${tune.LoadAddress:X4}-${end:X4} to ${relocated.LoadAddress:X4}-${movedEnd:X4}, zero page ${ZeroPageFirst:X2}+{zp}");
                return relocated;
            }
            catch (Exception ex)
            {
                log?.Invoke($"[SID] sidreloc failed: {ex.Message}");
                return tune;
            }
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
                Songs, StartSong, Flags, ZeroPage
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
