using System.Diagnostics;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE.Drive.Vice
{
    public class ViceSave : ISave
    {
        public ViceSave(IConfiguration configuration, ILog logger)
        {
            this.Configuration = configuration;
            this.Logger = logger;
        }
        private IConfiguration Configuration;
        private ILog Logger;

        SaveResponse ISave.Save(SaveRequest saveRequest, IFloppyResolver floppyResolver, byte[] payload)
        {
            SaveResponse saveResponse = new SaveResponse();
            saveResponse.ResponseCode = 0xff;

            byte[] destPtrFileData = new byte[payload.Length + 2];
            destPtrFileData[0] = saveRequest.TargetAddressLo;
            destPtrFileData[1] = saveRequest.TargetAddressHi;
            payload.CopyTo(destPtrFileData, 2);

            string fullPath = Path.Combine(this.Configuration.TempPath, this.Configuration.TempFolder);
            if (!Directory.Exists(fullPath))
                Directory.CreateDirectory(fullPath);

            string safeName = new string(saveRequest.FileName.TakeWhile(c => c != '\0').ToArray()).ToLowerInvariant();
            string tempPrgPath = Path.Combine(fullPath, safeName);

            File.WriteAllBytes(tempPrgPath, destPtrFileData);

            string fileSpec = $"@8:{safeName}";

            var psi = new ProcessStartInfo
            {
                FileName = this.Configuration.C1541Path,
                Arguments = $"\"{floppyResolver.GetInsertedFloppyPointer().Value.ImagePath}\" -write \"{tempPrgPath}\" \"{fileSpec}\" -quit",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var proc = Process.Start(psi))
            {
                string c1541Out = proc.StandardOutput.ReadToEnd();
                // Writing file -- use this for success check
                proc.WaitForExit();
                this.Logger.LogMessage(c1541Out.Trim());
            }

            bool success = File.Exists(tempPrgPath);
            if (success)
            {
                // TODO: parse errors and return if needed
                this.Logger.LogMessage($"File written to Image: {safeName}");
                File.Delete(tempPrgPath); // cleanup temp file
            }
            else
            {
                this.Logger.LogMessage($"ERROR: Failed to write {safeName} to Image.");
            }

            return saveResponse;
        }
    }
}
