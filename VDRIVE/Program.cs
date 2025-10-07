namespace VDRIVE
{
    public class Program
    {
        //  string petcatPath = @"C:\Programming\WinVICE-2.4-x64\petcat.exe"; // Adjust if needed
        // string d64Path = @"C:\Programming\Virtual Disks\UP9600_TST.d64"; // hard coded for now until I add wedge
        //string d64Path = @"C:\Programming\Virtual Disks\SIDs.d64"; // hard coded for now until I add wedge
        //string d64Path = @"C:\Programming\ROMS\WATERSKI_3D.d64"; // MULTILOAD partially works on this one!!!
         string d64Path = @"C:\Programming\Virtual Disks\data4.d64";
        //string d64Path = @"C:\Programming\Virtual Disks\1700_REU_Tset_Demo_Disk_19xx_Commodore.d64";
        //string d64Path = @"C:\Programming\ROMS\TestImages4VDisk\Super Mario Bros 64 v1.2 (Zeropaige).d64";
         //string d64Path = @"C:\Programming\ROMS\TestImages4VDisk\Works!\Bruce Lee - Duology.d64";
        //string d64Path = @"C:\Programming\ROMS\TestImages4VDisk\Fails\Action Biker '99 (Re-Hashed by Jon Wells).d64";

        static void Main(string[] args)
        {
            Program program = new Program();
            program.StartVDRIVEAsServer();
           // program.StartVDRIVEAsClient();
        }

        void StartVDRIVEAsServer()
        {
            Server server = new Server(d64Path);
            server.Start();
        }

        void StartVDRIVEAsClient()
        {
            Client client = new Client(d64Path);
            client.Start();
        }
    }
}
