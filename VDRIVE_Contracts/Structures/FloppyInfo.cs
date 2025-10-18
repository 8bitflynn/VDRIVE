using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [DebuggerDisplay("ImageName={new string(ImageName, 0, ImageNameLength)}, Id={(IdLo | IdHi << 8)}")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FloppyInfo
    {
        // FloppyIdentifier shown next to ImageName sent back when user selects this ID
        public byte IdLo;
        public byte IdHi;

        public byte ImageNameLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public char[] ImageName; // name of image / description           
    }
}
