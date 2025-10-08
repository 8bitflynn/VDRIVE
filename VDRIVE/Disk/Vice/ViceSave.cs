using System.Diagnostics;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Disk.Vice
{
    public class ViceSave : ISave
    {
        public ViceSave(string c1541Path, string tempPath = "")
        {
            this.C1541Path = c1541Path;
            if (string.IsNullOrEmpty(tempPath))
            {
                this.TempPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }
        }
        private readonly string C1541Path;
        private readonly string TempPath;

        SaveResponse ISave.Save(SaveRequest saveRequest, string imagePath, byte[] payload)
        {
            SaveResponse saveResponse = new SaveResponse();
            saveResponse.ResponseCode = 0xff;

            byte[] destPtrFileData = new byte[payload.Length + 2];
            destPtrFileData[0] = saveRequest.TargetAddressLo;
            destPtrFileData[1] = saveRequest.TargetAddressHi;
            payload.CopyTo(destPtrFileData, 2);

            if (!Directory.Exists(this.TempPath))
                Directory.CreateDirectory(this.TempPath);

            string safeName = new string(saveRequest.FileName.TakeWhile(c => c != '\0').ToArray()).ToLowerInvariant();
            string tempPrgPath = Path.Combine(this.TempPath, safeName);

            File.WriteAllBytes(tempPrgPath, destPtrFileData);

            string fileSpec = $"@8:{safeName}";

            var psi = new ProcessStartInfo
            {
                FileName = C1541Path,
                Arguments = $"\"{imagePath}\" -write \"{tempPrgPath}\" \"{fileSpec}\" -quit",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var proc = Process.Start(psi))
            {
                string c1541Out = proc.StandardOutput.ReadToEnd();
                // Writing file -- use this for success check
                proc.WaitForExit();
                Console.WriteLine(c1541Out);
            }

            bool success = File.Exists(tempPrgPath);
            if (success)
            {
                // TODO: parse errors and return if needed
                Console.WriteLine($"File written to Image: {safeName}");
                File.Delete(tempPrgPath); // cleanup temp file
            }
            else
            {
                Console.WriteLine($"ERROR: Failed to write {safeName} to Image.");
            }

            return saveResponse;
        }
    }
}
