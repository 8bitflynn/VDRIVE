using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures.Http
{
    [DebuggerDisplay("SessionId={SessionId}, ResultCount={ResultCount}")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HttpSearchResponse
    {
        public ushort SessionId;           // Session ID (lo/hi bytes)
        public ushort ResultCount;         // Total result count (lo/hi bytes)
                                           // Text payload follows this header

        // Helper method to create response with session
        public static HttpSearchResponse Create(ushort sessionId, ushort resultCount)
        {
            return new HttpSearchResponse
            {
                SessionId = sessionId,
                ResultCount = resultCount
            };
        }
    }
}