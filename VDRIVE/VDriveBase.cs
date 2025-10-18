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
        protected IVDriveLoggger Logger;

        protected void HandleClient(TcpClient tcpClient, NetworkStream networkStream, IFloppyResolver floppyResolver, IVDriveLoader loader, IVDriveSave saver)
        { 
            byte[] sendBuffer = new byte[1];
            byte[] data = new byte[1];
            if (!networkStream.DataAvailable)
            {
                Thread.Sleep(10); // avoid tight loop
                return;
            }

            int offset = networkStream.Read(data, 0, data.Length);
            if (offset == 1 && data[0] == 0x2b) // sync byte
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

                            this.ReadNetworkStream(networkStream, buffer, 1, size - 1, TimeSpan.FromSeconds(this.Configuration.ReceiveTimeoutSeconds.Value));

                            LoadRequest loadRequest = BinaryStructConverter.FromByteArray<LoadRequest>(buffer);
                            LoadResponse loadResponse = loader.Load(loadRequest, floppyResolver, out byte[] payload);

                            if (payload != null)
                            {
                                byte[] dest_ptr_bytes = payload.Take(2).ToArray();
                                ushort dest_ptr = (ushort)(dest_ptr_bytes[0] | (dest_ptr_bytes[1] << 8));
                                this.Logger.LogMessage($"Destination Address: 0x{dest_ptr:X4}");

                                if (dest_ptr == 0xEA38) 
                                {
                                    this.Logger.LogMessage("Warning: IRQ vector for load address");
                                }

                                payload = payload.Skip(2).ToArray(); // skip destination pointer     
                            }                            

                            this.SendData(tcpClient, networkStream, loadResponse, payload);
                        }
                        break;

                    case 0x02: // SAVE
                        {
                            int size = Marshal.SizeOf<SaveRequest>();

                            byte[] buffer = new byte[size];
                            buffer[0] = data[0];

                            this.ReadNetworkStream(networkStream, buffer, 1, size - 1, TimeSpan.FromSeconds(this.Configuration.ReceiveTimeoutSeconds.Value));

                            SaveRequest saveRequest = BinaryStructConverter.FromByteArray<SaveRequest>(buffer);

                            this.Logger.LogMessage($"Save Request: SAVE\"{new string(saveRequest.FileName)}\",{saveRequest.DeviceNum}{(saveRequest.SecondaryAddr != 0 ? "," + saveRequest.SecondaryAddr : "")}");

                            // recv data
                            byte[] payload = this.ReceiveData(networkStream, saveRequest);

                            // TODO send this back to C64
                            SaveResponse saveResponse = saver.Save(saveRequest, floppyResolver, payload);
                        }
                        break;

                    case 0x03: // INSERT/MOUNT floppy image
                        {
                            int size = Marshal.SizeOf<FloppyIdentifier>();

                            byte[] buffer = new byte[size];

                            this.ReadNetworkStream(networkStream, buffer, 0, size, TimeSpan.FromSeconds(this.Configuration.ReceiveTimeoutSeconds.Value));

                            FloppyIdentifier floppyIdentifier = BinaryStructConverter.FromByteArray<FloppyIdentifier>(buffer);
                            FloppyInfo floppyInfo = floppyResolver.InsertFloppy(floppyIdentifier);                                                  
                        }
                        break;

                    case 0x04: // UNMOUNT floppy image
                        {
                            // NOT USED
                            // TODO: not sure I will implement this as I do not really see a need to?
                            // the only reason one would eject a floppy is to insert another one
                            // and that is handled by the insert command

                            // MAYBE just re-use MOUNT sending in 0 instead of a real id?

                            // TODO: read params from C64
                            floppyResolver.EjectFloppy();
                        }
                        break;

                    case 0x05: // search for floppys
                        {
                            int size = Marshal.SizeOf<SearchFloppiesRequest>();

                            byte[] buffer = new byte[size];
                            buffer[0] = data[0]; // operation byte - fix

                            this.ReadNetworkStream(networkStream, buffer, 1, size - 1, TimeSpan.FromSeconds(this.Configuration.ReceiveTimeoutSeconds.Value));

                            SearchFloppiesRequest searchFloppiesRequest = BinaryStructConverter.FromByteArray<SearchFloppiesRequest>(buffer);

                            this.Logger.LogMessage("Search Request: " + new string(searchFloppiesRequest.SearchTerm) + (searchFloppiesRequest.MediaType != null ? "," + new string(searchFloppiesRequest.MediaType) : ""));

                            SearchFloppyResponse searchFloppyResponse = floppyResolver.SearchFloppys(searchFloppiesRequest, out FloppyInfo[] foundFloppyInfos);

                            // build the payload
                            List<byte> payload = new List<byte>();
                            foreach (FloppyInfo foundFloppyInfo in foundFloppyInfos)
                            {
                                byte[] serializedFoundFloppyInfo = BinaryStructConverter.ToByteArray<FloppyInfo>(foundFloppyInfo);
                                payload.AddRange(serializedFoundFloppyInfo);
                            }

                            int lengthOfPayload = payload.Count;
                            searchFloppyResponse.ByteCountLo = (byte)(lengthOfPayload & 0xFF); // LSB
                            searchFloppyResponse.ByteCountMid = (byte)((lengthOfPayload >> 8) & 0xFF);
                            searchFloppyResponse.ByteCountHi = (byte)((lengthOfPayload >> 16) & 0xFF); // MSB

                            this.SendData(tcpClient, networkStream, searchFloppyResponse, payload.ToArray());
                        }
                        break;
                }

                networkStream.Flush();
            }
        }
        
        protected bool ReadNetworkStream(NetworkStream stream, byte[] buffer, int offset, int count, TimeSpan timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int read = 0;

            while (read < count)
            {
                if (sw.Elapsed >= timeout)
                {
                    return false; // timed out before reading required bytes
                }

                if (!stream.DataAvailable)
                {
                    Thread.Sleep(10);
                    continue;
                }

                int r;
                try
                {
                    r = stream.Read(buffer, offset + read, count - read);
                }
                catch
                {
                    return false; // treat exceptions as failure to read
                }

                if (r <= 0) return false; // remote closed or no data
                read += r;
            }

            return true;
        }

        protected byte[] ReceiveData(NetworkStream networkStream, SaveRequest saveRequest)
        {
            int chunkSize = (saveRequest.ChunkSizeHi << 8) | saveRequest.ChunkSizeLo;
            int totalSize = (saveRequest.ByteCountHi << 16) | (saveRequest.ByteCountMid << 8) | saveRequest.ByteCountLo;

            byte[] buffer = new byte[totalSize];
            var oneByte = new byte[1];
            var ack = new byte[] { 0x2B, 0x01 }; // request next chunk
            int received = 0;

            var timeout = TimeSpan.FromSeconds(this.Configuration.ReceiveTimeoutSeconds.Value);
            var receiveTimeoutStopWatch = System.Diagnostics.Stopwatch.StartNew();

            // wait for initial sync byte '+' (0x2B)
            while (true)
            {
                if (receiveTimeoutStopWatch.Elapsed >= timeout)
                {
                    this.Logger.LogMessage("Timeout waiting for initial sync byte, aborting receive");
                    throw new TimeoutException("Timed out waiting for sync byte");
                }

                if (!networkStream.DataAvailable)
                {
                    Thread.Sleep(10);
                    continue;
                }

                int r = networkStream.Read(oneByte, 0, 1);
                if (r == 1 && oneByte[0] == 0x2B) break; // sync found
                                                         // else drop garbage and continue
            }

            int chunksReceived = 0;
            while (received < totalSize)
            {
                receiveTimeoutStopWatch.Restart();

                int toRead = Math.Min(chunkSize, totalSize - received);
                bool ok = ReadNetworkStream(networkStream, buffer, received, toRead, timeout);
                if (!ok)
                {
                    this.Logger.LogMessage($"Timeout or read error receiving chunk at offset {received}, aborting receive");
                    throw new TimeoutException("Timed out while reading chunk data");
                }

                received += toRead;

                try
                {
                    networkStream.Write(ack, 0, ack.Length);
                    networkStream.Flush();
                }
                catch (Exception ex)
                {
                    this.Logger.LogMessage($"Error sending ack: {ex.Message}, aborting receive");
                    throw;
                }

                chunksReceived++;
                this.Logger.LogMessage($"Received {received} of {totalSize} bytes in {chunksReceived} chunks");
            }

            return buffer;
        }       

        protected void SendData<T>(TcpClient tcpClient, NetworkStream networkStream, T header, byte[] payload) where T : struct
        {
            DateTime start = DateTime.Now;

            byte[] sendBuffer = new byte[1];

            // sync byte
            sendBuffer[0] = (byte)'+';
            networkStream.Write(sendBuffer, 0, 1);

            byte[] headerBytes = BinaryStructConverter.ToByteArray<T>(header);
            networkStream.Write(headerBytes, 0, headerBytes.Length);

            if (payload == null) // file not found or similar
            {
                return;
            }

            // pass the configured timeout
            this.SendChunks(tcpClient, networkStream, payload, this.Configuration.ChunkSize, TimeSpan.FromSeconds(this.Configuration.SendTimeoutSeconds.Value));

            DateTime end = DateTime.Now;
            this.Logger.LogMessage($"TimeTook:{(end - start).TotalMilliseconds / 1000} BytesPerSec:{payload.Length / (end - start).TotalMilliseconds * 1000}");

            networkStream.Flush();
        }

        private void SendChunks(TcpClient tcpClient, NetworkStream networkStream, byte[] payload, ushort chunkSize, TimeSpan timeout)
        {
            IList<List<byte>> chunks = payload.ToList().BuildChunks(chunkSize).ToList();
            int numOfChunks = chunks.Count();
            int bytesSent = 0;

            for (int chunkIndex = 0; chunkIndex < numOfChunks; chunkIndex++)
            {
                List<byte> chunk = chunks[chunkIndex];

                this.Logger.LogMessage($"{bytesSent}-{chunk.Count + bytesSent} chunk {chunkIndex + 1} of {chunks.Count}");

                try
                {
                    // send whole chunk (blocking)
                    networkStream.Write(chunk.ToArray(), 0, chunk.Count);

                    bytesSent += chunk.Count;
                    // flush synchronously to ensure bytes leave the buffer
                    networkStream.Flush();

                    // wait for Chunk request from C64 with timeout
                    ChunkRequest chunkRequest = this.ReadChunkRequest(tcpClient, networkStream, timeout);

                    switch (chunkRequest.Operation)
                    {
                        case 0x01: // next chunk or finished
                            if (chunkIndex + 1 == numOfChunks)
                            {
                                this.Logger.LogMessage("Send complete");
                                break;
                            }
                            break;

                        case 0x02: // resend last chunk
                            this.Logger.LogMessage("Resending last chunk");
                            chunkIndex--;
                            bytesSent -= chunk.Count;
                            if (bytesSent < 0) bytesSent = 0;
                            break;

                        case 0x03: // cancel
                            this.Logger.LogMessage("Send canceled");
                            return; // cancel this send
                    }
                }
                catch (TimeoutException)
                {
                    this.Logger.LogMessage($"Timeout waiting for chunk ack after sending bytes {bytesSent - chunk.Count}..{bytesSent}, aborting send");
                    SafeClose(tcpClient, networkStream);
                    return;
                }
                catch (Exception ex)
                {
                    this.Logger.LogMessage($"Error during send: {ex.Message}, aborting send");
                    SafeClose(tcpClient, networkStream);
                    return;
                }
            }
        }

        private ChunkRequest ReadChunkRequest(TcpClient tcpClient, NetworkStream networkStream, TimeSpan timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            byte[] data = new byte[1];

            while (tcpClient.Connected)
            {
                if (sw.Elapsed >= timeout)
                {
                    throw new TimeoutException("Timed out waiting for chunk request");
                }

                if (!networkStream.DataAvailable)
                {
                    // sleep short to avoid busy wait but check timeout frequently
                    Thread.Sleep(10);
                    continue;
                }

                int bytesRead = networkStream.Read(data, 0, 1);
                if (bytesRead == 1 && data[0] == 0x2b) // '+' sync
                {
                    if (sw.Elapsed >= timeout)
                    {
                        throw new TimeoutException("Timed out waiting for chunk request payload");
                    }

                    // read second byte (operation)
                    bytesRead = networkStream.Read(data, 0, 1);
                    if (bytesRead == 1)
                    {
                        // if ChunkRequest is larger than 1 byte, read the rest here
                        // Adjust to actual ChunkRequest size; example assumes struct is 1 byte for Operation
                        ChunkRequest chunkRequest = BinaryStructConverter.FromByteArray<ChunkRequest>(data);
                        return chunkRequest;
                    }
                }
                else
                {
                    // dump the garbage chars by design; continue waiting
                }
            }

            throw new InvalidOperationException("Connection closed while waiting for chunk request");
        }

        private void SafeClose(TcpClient tcpClient, NetworkStream networkStream)
        {
            try { networkStream?.Close(); } catch { }
            try { tcpClient?.Close(); } catch { }
        }
    }
}
