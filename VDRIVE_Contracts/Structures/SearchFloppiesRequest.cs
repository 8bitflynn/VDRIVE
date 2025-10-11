using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SearchFloppiesRequest
    {
        public byte SearchTermLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 80)]        
        public char[] SearchTerm; // search term padded with 0x00

        public byte MediaTypeLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public string MediaType; // D64/D71/D81
    }
}
