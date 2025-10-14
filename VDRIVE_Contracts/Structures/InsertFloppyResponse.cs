using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct InsertFloppyResponse
    {
        public byte ResponseCode;          // 0x00 = success, # = error number to show
    }
}
