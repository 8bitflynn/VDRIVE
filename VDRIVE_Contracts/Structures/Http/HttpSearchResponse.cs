using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures.Http
{
    [DebuggerDisplay("SessionId={SessionId}, ResultCount={ResultCount}")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HttpSearchResponse
    {
        public ushort SessionId;           // Session ID (lo/hi bytes)
        public byte ResultCount;           // Number of results found (0 if none)
                                          // Text payload follows this header

        // Helper method to create response with session
        public static HttpSearchResponse Create(ushort sessionId, byte resultCount)
        {
            return new HttpSearchResponse
            {
                SessionId = sessionId,
                ResultCount = resultCount
            };
        }
    }
}