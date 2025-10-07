using System.Runtime.InteropServices;

namespace VDRIVE.Structures
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ChunkHeader
    {
        public byte CheckSumLo;
        public byte CheckSumHi;
    }
}
