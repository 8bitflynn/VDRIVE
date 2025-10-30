using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NewFloppyRequest
    {
        public byte NewFloppyParamsLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 80)] // Physical filename[space]Floppy Name[space]Image Type (optionally uses appsettings default)
        public char[] NewFloppyParams;       
    }
}
