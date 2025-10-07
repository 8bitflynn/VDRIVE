using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LoadResponse
    {
        public byte ResponseCode;          // 0x00 = success, # = error number to show

        public byte SyncByte;

        // send binary length in 16 bits
        public byte ByteCountLo;
        public byte ByteCountHi;

        public byte ChunkSizeLo;
        public byte ChunkSizeHi;

        public byte DestPtrLo;
        public byte DestPtrHi;
    }
}
