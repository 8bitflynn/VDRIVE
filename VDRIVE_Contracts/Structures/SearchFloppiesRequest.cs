using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [DebuggerDisplay("SearchTermLength={SearchTermLength}, SearchTerm={new string(SearchTerm)}, MediaTypeLength={MediaTypeLength}, MediaType={MediaType}")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SearchFloppiesRequest
    {
        public byte SearchTermLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 80)]        
        public char[] SearchTerm; // search term padded with 0x00

        public byte MediaTypeLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public string MediaType; // optional - D64/D71/D81

        public byte Flags; // dependent on implementation but can be used for ordering, case sensitivity, etc.
    }
}
