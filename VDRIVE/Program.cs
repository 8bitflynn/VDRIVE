namespace VDRIVE
{
    public class Program
    {
        // stop gap until I get the mount/unmount added       
        string d64Path = @"C:\Programming\Virtual Disks\data5.d64";
        //string d64Path = @"C:\Programming\ROMS\TestImages4VDisk\Works!\Bruce Lee - Duology.d64";
        //string d64Path = @"C:\Programming\ROMS\TestImages4VDisk\Fails\Action Biker '99 (Re-Hashed by Jon Wells).d64";

        static void Main(string[] args)
        {
            Program program = new Program();

            // firmware is setup as client by default so run this in server mode
            program.StartVDRIVEAsServer();

            // cant open firewall on computer? change firmware to run as server and this as client
            // no firewalls on ESP8266
            //program.StartVDRIVEAsClient();
        }       

        void StartVDRIVEAsServer()
        {
            // TODO: inject dependencies for ILoad/ISave and whatever the mount/unmount will be called
            Server server = new Server(d64Path);
            server.Start();
        }

        void StartVDRIVEAsClient()
        {
            string ipAddress = "192.168.1.38";
            int port = 80;

            // TODO: inject dependencies for ILoad/ISave and whatever the mount/unmount will be called
            Client client = new Client(d64Path, ipAddress, port);
            client.Start();
        }
    }
}
