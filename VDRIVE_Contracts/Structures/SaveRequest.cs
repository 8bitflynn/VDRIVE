using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SaveRequest
    {
        public byte Operation;          // 0x02 = SAVE

        public byte FileNameLength;     // Actual filename length (0–255)
        public byte LogicalFileNum;     // From $B8
        public byte SecondaryAddr;      // From $B9
        public byte DeviceNum;          // From $BA

        public byte TargetAddressLo;    // From $A0/$A1 (little-endian)
        public byte TargetAddressHi;

        public byte ByteCountLo;
        public byte ByteCountMid;
        public byte ByteCountHi;

        public byte ChunkSizeLo;
        public byte ChunkSizeHi;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public char[] FileName;         // Filename in PETSCII, padded with $20 or $00
    }
}
