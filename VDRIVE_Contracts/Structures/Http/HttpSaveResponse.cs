using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures.Http
{
    [DebuggerDisplay("SessionId={SessionId}, ResponseCode={ResponseCode}")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HttpSaveResponse
    {
        public ushort SessionId;           // Session ID (lo/hi bytes)
        public byte ResponseCode;          // 0x00 = success, # = error number
                                           // Text payload follows this header

        // Helper method to create response with session
        public static HttpSaveResponse Create(ushort sessionId, byte responseCode)
        {
            return new HttpSaveResponse
            {
                SessionId = sessionId,
                ResponseCode = responseCode
            };
        }
    }
}
