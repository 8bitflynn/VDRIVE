using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ChunkRequest
    {
        public byte Operation;  // 0x01=Next Chunk, 0x02=LastChunk; 0x03=Cancel
    }
}
