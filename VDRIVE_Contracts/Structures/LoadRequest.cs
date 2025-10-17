using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [DebuggerDisplay("ImageName={new string(FileName, 0, FileNameLength)}, DeviceNum={DeviceNum}")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LoadRequest
    {
        public byte Operation;          // 0x01 = LOAD

        public byte FileNameLength;     // Actual filename length (0–255)
        public byte LogicalFileNum;     // From $B8
        public byte SecondaryAddr;      // From $B9
        public byte DeviceNum;          // From $BA

        public byte LoadMode;           // From $A7 (0 = use file header, 1 = override)
        public ushort TargetAddress;    // From $A0/$A1 (little-endian)

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public char[] FileName;         // Filename in PETSCII, padded with $20 or $00
    }
}
