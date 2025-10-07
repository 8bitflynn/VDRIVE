using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ChunkHeader
    {
        public byte CheckSumLo;
        public byte CheckSumHi;
    }
}
