using System.Net.Sockets;

namespace VDRIVE_Contracts.Interfaces
{
    public interface IProtocolHandler
    {
        void HandleClient(IFloppyResolver floppyResolver, IStorageAdapter storageAdapter);
    }

}
