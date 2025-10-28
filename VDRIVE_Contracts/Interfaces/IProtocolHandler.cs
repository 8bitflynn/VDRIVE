﻿using System.Net.Sockets;

namespace VDRIVE_Contracts.Interfaces
{
    public interface IProtocolHandler
    {
        void HandleClient(TcpClient tcpClient, NetworkStream networkStream, IFloppyResolver floppyResolver, IStorageAdapter storageAdapter);
    }

}
