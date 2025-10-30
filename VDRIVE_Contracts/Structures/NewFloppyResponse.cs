using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct NewFloppyResponse
    {
        public byte ResponseCode;
    }
}
