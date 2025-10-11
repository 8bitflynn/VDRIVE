using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FloppyInfo
    {
        public byte ImagePathLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 255)]
        public char[] ImagePath; // full path to image
        public string Id; // specific to IFloppyResolver implementation

        public byte DescriptionLengthLo;
        public byte DescriptionLengthHi;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1024)]
        public char[] Description;

        public byte MediaTypeLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public char[] MediaType; // D64/D71/D81

        public byte Flags;         
    }
}
