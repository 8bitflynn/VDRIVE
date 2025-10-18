using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FloppyIdentifier // send back from C64 to identify floppy to insert
    {
        public byte IdLo;
        public byte IdHi;
    }
}
