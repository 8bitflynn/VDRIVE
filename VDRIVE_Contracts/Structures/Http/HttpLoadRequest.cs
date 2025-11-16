using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VDRIVE_Contracts.Structures.Http
{
    [DebuggerDisplay("SessionId={SessionId}, FilenameLength={FilenameLength}, Filename={GetFilenameString()}")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class HttpLoadRequest
    {
        public ushort SessionId;
        public byte FilenameLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Filename; // filename padded with 0x00

        public static HttpLoadRequest ParseFromBytes(byte[] data)
        {
            if (data.Length < 3) throw new ArgumentException("Invalid data");

            ushort sessionId = (ushort)(data[0] | (data[1] << 8));
            byte filenameLength = data[2];

            if (data.Length < 3 + filenameLength) throw new ArgumentException("Invalid filename length");

            var request = new HttpLoadRequest();
            request.SessionId = sessionId;
            request.FilenameLength = filenameLength;
            request.Filename = new byte[32]; // Initialize full array

            // Copy filename data
            if (filenameLength > 0)
            {
                Array.Copy(data, 3, request.Filename, 0, Math.Min((int)filenameLength, 32));
            }

            return request;
        }

        public string GetFilenameString()
        {
            if (Filename == null || FilenameLength == 0)
                return string.Empty;
                
            return Encoding.ASCII.GetString(Filename, 0, FilenameLength);
        }
    }
}
