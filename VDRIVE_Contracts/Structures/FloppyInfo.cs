using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [DebuggerDisplay("ImageName={new string(ImageName, 0, ImageNameLength)}, Id={(IdLo | IdHi << 8)}, Description={new string(Description, 0, DescriptionLength)}")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FloppyInfo
    {
        public byte ImageNameLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public char[] ImageName; // name of image only

        // floppy identifier sent back from C64 to insert floppy
        public byte IdLo;
        public byte IdHi;

        public byte DescriptionLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 255)]
        public char[] Description; // optional description                  
    }
}
