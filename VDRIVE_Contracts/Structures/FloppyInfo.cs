using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [DebuggerDisplay("ImageName={new string(ImageName, 0, ImageNameLength)}, Id={(IdLo | IdHi << 8)}")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FloppyInfo
    {
        // floppy identifier sent back from C64 to insert floppy
        public byte IdLo;
        public byte IdHi;

        public byte ImageNameLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public char[] ImageName; // name of image / description           
    }
}
