using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SearchFloppiesRequest
    {
        public byte DescriptionLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 255)]        
        public char[] Description; // search term padded with 0x00

        public byte MediaTypeLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
        public string MediaType; // D64/D71/D81
    }
}
