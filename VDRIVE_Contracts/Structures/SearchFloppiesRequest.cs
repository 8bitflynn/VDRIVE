using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [DebuggerDisplay("SearchTermLength={SearchTermLength}, SearchTerm={new string(SearchTerm)}, MediaTypeLength={MediaTypeLength}, MediaType={MediaType}")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SearchFloppiesRequest
    {
        public byte Operation;         // 0x05 = SEARCH for Floppies

        public byte SearchTermLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 80)]        
        public char[] SearchTerm; // search term padded with 0x00

        // DEV NOTE: currently not used / sent by C64 - just padded with 0x00
        public byte MediaTypeLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public char[] MediaType; // optional CSV of media types (.D64,.D81) padded with 0x00 (uses config default if not filled in)

        public byte Flags; // dependent on implementation but can be used for ordering, case sensitivity, etc.
    }
}
