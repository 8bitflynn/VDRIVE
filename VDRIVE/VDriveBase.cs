using System.Net.Sockets;
using System.Runtime.InteropServices;
using VDRIVE.Util;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE
{
    public abstract class VDriveBase
    {
        protected IConfiguration Configuration;
        protected IFloppyResolver FloppyResolver;
        protected ILoad Loader;
        protected ISave Saver;
        protected ILog Logger;

        protected void HandleClient(TcpClient tcpClient, NetworkStream networkStream)
        {
            byte[] sendBuffer = new byte[1];
            byte[] data = new byte[1];
            if (!networkStream.DataAvailable)
            {
                Thread.Sleep(10); // avoid tight loop
                return;
            }

            int offset = networkStream.Read(data, 0, data.Length);
            if (offset == 1 && data[0] == 0x2b)
            {
                // operation byte
                offset = networkStream.Read(data, 0, data.Length);

                switch (data[0])
                {
                    case 0x01: // LOAD
                        {
                            int size = Marshal.SizeOf<LoadRequest>();

                            byte[] buffer = new byte[size];
                            buffer[0] = data[0];

                            this.ReadNetworkStream(networkStream, buffer, 1, size - 1);

                            LoadRequest loadRequest = BinaryStructConverter.FromByteArray<LoadRequest>(buffer);                       
                            LoadResponse loadResponse = this.Loader.Load(loadRequest, this.FloppyResolver.GetInsertedFloppyPath(), out byte[] payload);

                            this.SendData(tcpClient, networkStream, loadRequest, loadResponse, payload);
                        }
                        break;

                    case 0x02: // SAVE
                        {
                            int size = Marshal.SizeOf<SaveRequest>();

                            byte[] buffer = new byte[size];
                            buffer[0] = data[0];

                            this.ReadNetworkStream(networkStream, buffer, 1, size - 1);

                            SaveRequest saveRequest = BinaryStructConverter.FromByteArray<SaveRequest>(buffer);

                            // recv data
                            byte[] payload = this.ReceiveData(networkStream, saveRequest);

                            this.Saver.Save(saveRequest, this.FloppyResolver.GetInsertedFloppyPath(), payload);                           
                        }
                        break;

                    case 0x03: // MOUNT floppy image
                        {    
                            // TODO: read path from C64
                            this.FloppyResolver.InsertFloppyByPath("");                           
                        }
                        break;

                    case 0x04: // UNMOUNT floppy image
                        {
                            this.FloppyResolver.EjectFloppy();
                        }
                        break;
                }

                networkStream.Flush();
            }
        }

        protected bool ReadNetworkStream(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            int read = 0;
            while (read < count)
            {
                int r = stream.Read(buffer, offset + read, count - read);
                if (r <= 0) return false;
                read += r;
            }
            return true;
        }        
       
        protected byte[] ReceiveData(NetworkStream networkStream, SaveRequest saveRequest, Action<int, int, int, int> progressCallback = null)
        {
            ushort byteCount = (ushort)((saveRequest.ByteCountHi << 8) + saveRequest.ByteCountLo);

            byte[] data = new byte[1];
            while (true)
            {
                if (!networkStream.DataAvailable)
                {
                    Thread.Sleep(10);
                    continue;
                }

                // sync byte
                Int32 bytes = networkStream.Read(data, 0, 1);
                if (bytes == 1 && data[0] == 0x2b)
                {
                    byte[] buffer = new byte[byteCount];
                    this.ReadNetworkStream(networkStream, buffer, 0, buffer.Length);
                    return buffer;
                }
                else
                {
                    // dump the garbage chars
                }
            }
        }

        protected void SendData(TcpClient tcpClient, NetworkStream networkStream, LoadRequest loadRequest, LoadResponse loadResponse, byte[] payload)
        {
            DateTime start = DateTime.Now;
            byte[] sendBuffer = new byte[1];

            // send chunk size 16 bits
            short chunkSize = 1024;

            // sync byte
            sendBuffer[0] = (byte)'+';
            networkStream.Write(sendBuffer, 0, 1);

            byte[] headerBytes = BinaryStructConverter.ToByteArray<LoadResponse>(loadResponse);

            networkStream.Write(headerBytes, 0, headerBytes.Length);

            if (payload == null) // file not found or something
            {
                return;
            }

            this.SendChunks(tcpClient, networkStream, payload, chunkSize);

            DateTime end = DateTime.Now;
            this.Logger.LogMessage($"TimeTook:{(end - start).TotalMilliseconds / 1000} BytesPerSec:{payload.Length / (end - start).TotalMilliseconds * 1000}");

            networkStream.Flush();
        }

        private void SendChunks(TcpClient tcpClient, NetworkStream networkStream, byte[] payload, short chunkSize)
        {
            IList<List<byte>> chunks = payload.ToList().BuildChunks(chunkSize).ToList();
            // stripping the first 2 bytes on first batch sent in header
            chunks[0] = chunks[0].Skip(2).ToList();

            int numOfChunks = chunks.Count();

            int bytesSent = 2; // account for mem header

            for (int chunkIndex = 0; chunkIndex < numOfChunks; chunkIndex++)
            {
                List<byte> chunk = chunks[chunkIndex];

                this.Logger.LogMessage($"{bytesSent} of {chunk.Count + bytesSent} chunk {chunkIndex + 1} of {chunks.Count}");

                networkStream.Write(chunk.ToArray(), 0, chunk.Count);

                bytesSent += chunk.Count;
                networkStream.FlushAsync();

                if (chunkIndex + 1 == numOfChunks)
                {
                    // transfer done
                    break;
                }

                // wait for Chunk request
                ChunkRequest chunkRequest = this.ReadChunkRequest(tcpClient, networkStream, ref chunkIndex);
                if (chunkRequest.Operation == 0x01)
                {
                    //break; // continue to next batch
                }
                else if (chunkRequest.Operation == 0x02)
                {
                    // NOT implemented - resend last batch
                    chunkIndex--;
                }
                else if (chunkRequest.Operation == 0x03)
                {
                    this.Logger.LogMessage("Canceling send");
                    return; // cancel this send
                }
            }
        }

        private ChunkRequest ReadChunkRequest(TcpClient tcpClient, NetworkStream networkStream, ref int chunkIndex)
        {
            byte[] data = new byte[1];
            while (tcpClient.Connected)
            {
                if (!networkStream.DataAvailable)
                {
                    Thread.Sleep(50);
                    continue;
                }
                Int32 bytes = networkStream.Read(data, 0, 1);

                if (bytes == 1 && data[0] == 0x2b)
                {
                    bytes = networkStream.Read(data, 0, 1);
                    if (bytes == 1)
                    {
                        ChunkRequest chunkRequest = BinaryStructConverter.FromByteArray<ChunkRequest>(data);
                        return chunkRequest;
                    }
                }
                else
                {
                    // dump the garbage chars
                }
            }

            return default(ChunkRequest);
        }
    }
}
