using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures.Http
{
    [DebuggerDisplay("SessionId={SessionId}, ResponseCode={ResponseCode}, PayloadLength={PayloadLength}")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HttpLoadResponse
    {
        public ushort SessionId;           // Session ID (lo/hi bytes)
        public byte ResponseCode;          // 0x00 = success, # = error number
                                           // File payload follows this header

        // Helper method to create response with session
        public static HttpLoadResponse Create(ushort sessionId, byte responseCode)
        {
            return new HttpLoadResponse
            {
                SessionId = sessionId,
                ResponseCode = responseCode
            };
        }
    }
}
