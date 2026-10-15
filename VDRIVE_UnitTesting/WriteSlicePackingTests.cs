using VDRIVE.Protocol;

namespace VDRIVE_UnitTesting
{
    // ",W" slices with empty sectors go packed (PatchStubs.BuildWriteSlice) and the write stub expands them in place
    // (stubs\patch_write.asm, unpack). These tests run the stub's unpack step by step on a 64K memory, with the packed
    // slice where the C64 loads it, so a packed byte overwritten before it is read shows up as a wrong result.
    [TestClass]
    public class WriteSlicePackingTests
    {
        private const int Buffer = 0x1000, Tables = 0x9000;

        [TestMethod]
        public void Packing_SyntheticDisks_ComeBackExactly()
        {
            var random = new Random(64);
            foreach (byte[] image in SyntheticImages(random))
                AssertRoundTrip(image, "synthetic " + image.Length);
        }

        [TestMethod]
        public void Packing_RawSliceAndProgram_AreUnchanged()
        {
            byte[] full = new byte[174848];
            new Random(1).NextBytes(full);                              // no empty sector anywhere
            byte[] slice = PatchStubs.BuildWriteSlice(full, 0, pack: true)!;
            Assert.AreEqual(Buffer, slice[0] | slice[1] << 8, "a slice without empty sectors loads at $1000");
            Assert.AreEqual(PatchStubs.WriteSliceSize + 2, slice.Length);

            byte[] zeros = new byte[40000];                             // a program: never packed, even all zeros
            byte[] prg = PatchStubs.BuildWriteSlice(zeros, 0, pack: false)!;
            Assert.AreEqual(Buffer, prg[0] | prg[1] << 8);
            Assert.AreEqual(PatchStubs.WriteSliceSize + 2, prg.Length);
        }

        // Every D64 under the folders in VDRIVE_TEST_D64_DIRS (separated by ';'), when that is set
        [TestMethod]
        public void Packing_RealDisks_ComeBackExactly()
        {
            string? dirs = Environment.GetEnvironmentVariable("VDRIVE_TEST_D64_DIRS");
            if (string.IsNullOrWhiteSpace(dirs)) Assert.Inconclusive("VDRIVE_TEST_D64_DIRS not set");
            int disks = 0;
            long raw = 0, sent = 0;
            foreach (string dir in dirs!.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            foreach (string path in Directory.EnumerateFiles(dir, "*.d64", SearchOption.AllDirectories))
            {
                byte[] image = File.ReadAllBytes(path);
                if (image.Length == 0 || image.Length > 26 * PatchStubs.WriteSliceSize) continue;
                sent += AssertRoundTrip(image, path);
                raw += image.Length;
                disks++;
            }
            Console.WriteLine($"{disks} disks: {raw} bytes, sent {sent} ({100.0 * (raw - sent) / raw:F1}% less)");
        }

        // Builds every slice the way the server sends it, expands it the way the stub does, and checks the result
        // against the image. Returns the bytes sent (without the PRG load addresses).
        private static long AssertRoundTrip(byte[] image, string what)
        {
            int slices = (image.Length + PatchStubs.WriteSliceSize - 1) / PatchStubs.WriteSliceSize;
            long sent = 0;
            for (int s = 0; s < slices; s++)
            {
                int length = Math.Min(PatchStubs.WriteSliceSize, image.Length - s * PatchStubs.WriteSliceSize);
                byte[] prg = PatchStubs.BuildWriteSlice(image, s, pack: true)!;
                int empty = PatchStubs.CountEmptySectors(image, s);      // what the stub's table (p_fills) holds
                int address = prg[0] | prg[1] << 8;
                sent += prg.Length - 2;

                byte[] memory = new byte[0x10000];
                new Random(s).NextBytes(memory);                        // whatever was in memory before
                Assert.IsTrue(address >= Buffer && address + prg.Length - 2 <= Tables, $"{what} slice {s}: outside the buffer");
                Array.Copy(prg, 2, memory, address, prg.Length - 2);
                Assert.AreEqual(Buffer + length, address + prg.Length - 2, $"{what} slice {s}: doesn't end where the slice ends");

                if (empty > 0) Unpack(memory, empty, length);
                else Assert.AreEqual(Buffer, address, $"{what} slice {s}: a plain slice not at $1000");

                for (int i = 0; i < length; i++)
                    if (memory[Buffer + i] != image[s * PatchStubs.WriteSliceSize + i])
                        Assert.Fail($"{what} slice {s}: byte {i} is ${memory[Buffer + i]:X2}, should be ${image[s * PatchStubs.WriteSliceSize + i]:X2}");
            }
            return sent;
        }

        // patch_write.asm "unpack", step by step: A = empty sectors, count = slice length
        private static void Unpack(byte[] memory, int a, int count)
        {
            int usrc = 0x0FF0 + 256 * a - a;
            byte[] ubits = new byte[16];
            for (int y = 15; y >= 0; y--) ubits[y] = memory[usrc + y];
            usrc += 16;
            int ptr = Buffer;
            int usect = (count >> 8) + ((count & 0xFF) != 0 ? 1 : 0);
            byte ubyte = 0;
            for (int usn = 0; usn < usect; usn++)
            {
                if ((usn & 7) == 0) ubyte = ubits[usn >> 3];
                bool isEmpty = (ubyte & 1) != 0;
                ubyte >>= 1;
                if (isEmpty)
                {
                    byte value = memory[usrc++];
                    for (int y = 0; y < 256; y++) memory[ptr + y] = value;
                }
                else
                {
                    for (int y = 0; y < 256; y++) memory[ptr + y] = memory[usrc + y];   // forward, one byte at a time
                    usrc += 256;
                }
                ptr += 256;
            }
        }

        private static IEnumerable<byte[]> SyntheticImages(Random random)
        {
            int[] sizes = { 174848, 175531, 196608, 40 * 256, 256, 300, 32768, 32768 + 256, 819200 };   // D64, D64 + errors, 40 tracks, ...
            foreach (int size in sizes)
            {
                yield return new byte[size];                            // all empty
                byte[] noise = new byte[size]; random.NextBytes(noise); yield return noise;   // nothing empty

                // every empty/full pattern in the first sectors, then random mixes with other fill values
                for (int pattern = 0; pattern < 64; pattern++)
                {
                    byte[] image = new byte[size];
                    random.NextBytes(image);
                    for (int sector = 0; sector * 256 + 256 <= size; sector++)
                    {
                        bool empty = sector < 6 ? (pattern >> sector & 1) != 0 : random.Next(3) == 0;
                        if (empty) Array.Fill(image, sector % 5 == 0 ? (byte)0x01 : (byte)0x00, sector * 256, 256);
                    }
                    yield return image;
                }

                // empty sectors only at the end of each slice, and only at the start: the tightest cases for in place
                byte[] tailEmpty = new byte[size]; random.NextBytes(tailEmpty);
                byte[] headEmpty = new byte[size]; random.NextBytes(headEmpty);
                for (int sector = 0; sector * 256 + 256 <= size; sector++)
                {
                    int inSlice = sector % 128;
                    if (inSlice >= 120) Array.Fill(tailEmpty, (byte)0, sector * 256, 256);
                    if (inSlice < 8) Array.Fill(headEmpty, (byte)0, sector * 256, 256);
                }
                yield return tailEmpty;
                yield return headEmpty;
            }
        }
    }
}
