using System.IO;
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
                            LoadResponse loadResponse = this.Loader.Load(loadRequest, this.FloppyResolver, out byte[] payload);

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

                            this.Logger.LogMessage($"Save Request: SAVE\"{new string(saveRequest.FileName)}\",{saveRequest.DeviceNum}{(saveRequest.SecondaryAddr != 0 ? "," + saveRequest.SecondaryAddr : "")}");

                            // recv data
                            byte[] payload = this.ReceiveData(networkStream, saveRequest);

                            SaveResponse saveResponse = this.Saver.Save(saveRequest, this.FloppyResolver, payload);
                        }
                        break;

                    case 0x03: // MOUNT floppy image
                        {
                            // TODO: read path from C64
                            InsertFloppyRequest insertFloppyRequest = new InsertFloppyRequest();

                            FloppyInfo floppyInfo = new FloppyInfo();
                            floppyInfo.Id = insertFloppyRequest.Id;
                            floppyInfo.ImagePath = insertFloppyRequest.ImagePath;

                            this.FloppyResolver.InsertFloppy(floppyInfo);
                        }
                        break;

                    case 0x04: // UNMOUNT floppy image
                        {
                            // not sure I need any params here
                            EjectFloppyRequest ejectFloppyRequest = new EjectFloppyRequest();

                            // TODO: read params from C64
                            this.FloppyResolver.EjectFloppy();
                        }
                        break;

                    case 0x05: // search for floppys
                        {
                            // TODO: read search params from C64
                            SearchFloppiesRequest searchFloppiesRequest = new SearchFloppiesRequest();

                            this.FloppyResolver.SearchFloppys(searchFloppiesRequest);
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

        protected byte[] ReceiveData(NetworkStream networkStream, SaveRequest saveRequest)
        {
            int chunkSize = (saveRequest.ChunkSizeHi << 8) | saveRequest.ChunkSizeLo;
            int totalSize =  (saveRequest.ByteCountHi << 16) | (saveRequest.ByteCountMid << 8) | saveRequest.ByteCountLo;           

            byte[] buffer = new byte[totalSize];
            var one = new byte[1];
            var ack = new byte[] { 0x2B, 0x01 };
            int received = 0;

            while (true)
            {
                if (!networkStream.DataAvailable)
                {
                    Thread.Sleep(10);
                    continue;
                }

                int r = networkStream.Read(one, 0, 1);
                if (r == 1 && one[0] == 0x2B) break; // sync byte

                // ignore garbage and continue
            }

            int chunksReceived = 0;
            while (received < totalSize)
            {
                int toRead = Math.Min(chunkSize, totalSize - received);
                ReadNetworkStream(networkStream, buffer, received, toRead);

                received += toRead;

                // ACK this chunk and request next chunk
                networkStream.Write(ack, 0, ack.Length);
                networkStream.Flush();

                chunksReceived++;

                this.Logger.LogMessage($"Received {received} of {totalSize} bytes in {chunksReceived} chunks");
            }           

            return buffer.ToArray();
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

                // wait for Chunk request
                ChunkRequest chunkRequest = this.ReadChunkRequest(tcpClient, networkStream, ref chunkIndex);
                switch (chunkRequest.Operation)
                {
                    case 0x01: // next chunk or finished
                        if (chunkIndex + 1 == numOfChunks)
                        {
                            // transfer done successfully
                            this.Logger.LogMessage("Send complete");
                            break;
                        }
                        break;

                    case 0x02: // resend last chunk
                        this.Logger.LogMessage("Resending last chunk");
                        chunkIndex--; 
                        break;

                    case 0x03: // cancel
                        this.Logger.LogMessage("Send canceled");
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
