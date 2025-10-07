namespace VDRIVE
{
    public class Program
    {
         string d64Path = @"C:\Programming\Virtual Disks\data5.d64";     
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
