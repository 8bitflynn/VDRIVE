using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VDRIVE_Contracts.Structures.Http
{
    [DebuggerDisplay("SearchTermLength={SearchTermLength}, SearchTerm={new string(SearchTerm)}")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class HttpSearchFloppyRequest
    {
        public ushort SessionId;
        public byte SearchTermLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public char[] SearchTerm; // search term padded with 0x00

        public static HttpSearchFloppyRequest ParseFromBytes(byte[] data)
        {
            if (data.Length < 3) throw new ArgumentException("Invalid data");

            ushort sessionId = (ushort)(data[0] | (data[1] << 8));
            byte searchTermLength = data[2];

            var request = new HttpSearchFloppyRequest();
            request.SessionId = sessionId;
            request.SearchTermLength = searchTermLength;
            request.SearchTerm = new char[32]; // Initialize full array

            // Copy actual search term data
            if (searchTermLength > 0 && data.Length >= 3 + searchTermLength)
            {
                Array.Copy(data, 3, request.SearchTerm, 0, Math.Min((int)searchTermLength, 32));
            }

            return request;
        }
    }


}
