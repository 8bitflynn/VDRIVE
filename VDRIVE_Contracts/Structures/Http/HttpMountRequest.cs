using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace VDRIVE_Contracts.Structures.Http
{
    [DebuggerDisplay("SessionId={SessionId}, ImageIdLength={ImageIdLength}, ImageId={GetImageIdString()}")]
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public class HttpMountRequest
    {
        public ushort SessionId;
        public byte ImageIdOrFileNameLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] ImageIdOrFilename; // image ID padded with 0x00

        public static HttpMountRequest ParseFromBytes(byte[] data)
        {
            if (data.Length < 3) throw new ArgumentException("Invalid data");

            ushort sessionId = (ushort)(data[0] | (data[1] << 8));
            byte imageIdLength = data[2];

            var request = new HttpMountRequest();
            request.SessionId = sessionId;
            request.ImageIdOrFileNameLength = imageIdLength;
            request.ImageIdOrFilename = new byte[32]; // Initialize full array

            // Copy actual image ID data
            if (imageIdLength > 0 && data.Length >= 3 + imageIdLength)
            {
                Array.Copy(data, 3, request.ImageIdOrFilename, 0, Math.Min((int)imageIdLength, 16));
            }

            return request;
        }

        public string GetImageIdOrFilenameString()
        {
            if (ImageIdOrFilename == null || ImageIdOrFileNameLength == 0)
                return string.Empty;
                
            return Encoding.ASCII.GetString(ImageIdOrFilename, 0, ImageIdOrFileNameLength);
        }
    }
}
