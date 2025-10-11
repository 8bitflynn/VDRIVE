using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct FloppyIdentifier
    {
        public byte IdLo;
        public byte IdHi;
    }
}
