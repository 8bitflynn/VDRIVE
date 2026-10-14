namespace VDRIVE.Util
{
    // Reads a whole file the way File.ReadAllBytes does, but shared: VDRIVE only reads disk images, so another program
    // that has the same file open - VICE with the D64 attached - doesn't block it ("being used by another process").
    // That stopped a ,W halfway (?VDRIVE LOAD ERROR) when the game was also open in VICE.
    public static class SharedFile
    {
        public static byte[] ReadAllBytes(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] bytes = new byte[stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = stream.Read(bytes, read, bytes.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                if (read < bytes.Length) Array.Resize(ref bytes, read);   // the file shrank while we read it
                return bytes;
            }
        }
    }
}
