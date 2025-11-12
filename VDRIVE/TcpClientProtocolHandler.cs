using System.Net.Sockets;
using System.Runtime.InteropServices;
using VDRIVE.Util;
using VDRIVE_Contracts.Enums;
using VDRIVE_Contracts.Interfaces;
using VDRIVE_Contracts.Structures;

namespace VDRIVE
{
    public class TcpClientProtocolHandler : IProtocolHandler
    {
        public TcpClientProtocolHandler(IConfiguration configuration, ILogger logger, TcpClient tcpClient, NetworkStream networkStream)
        {
            this.Configuration = configuration;
            this.Logger = logger;
            this.tcpClient = tcpClient;
            this.networkStream = networkStream;
        }
        private IConfiguration Configuration;
        private ILogger Logger;
        private TcpClient tcpClient;
        private NetworkStream networkStream;  

        public void HandleClient(IFloppyResolver floppyResolver, IStorageAdapter storageAdapter)
        {
            byte[] data = new byte[1];
            if (!networkStream.DataAvailable)
            {
                Thread.Sleep(10); // avoid tight loop
                return;
            }

            bool success = this.ReadNetworkStream(networkStream, data, 0, 1, TimeSpan.FromSeconds(this.Configuration.ReceiveTimeoutSeconds.Value));
            if (success && data[0] == 0x2b) // sync byte
            {
                // operation byte
                success = this.ReadNetworkStream(networkStream, data, 0, 1, TimeSpan.FromSeconds(this.Configuration.ReceiveTimeoutSeconds.Value));
                if (!success)
                    return;

                switch (data[0])
                {
                    case 0x01: // LOAD
                        {
                            int size = Marshal.SizeOf<LoadRequest>();

                            byte[] buffer = new byte[size];
                            buffer[0] = data[0];

                            success = this.ReadNetworkStream(networkStream, buffer, 1, size - 1, TimeSpan.FromSeconds(this.Configuration.ReceiveTimeoutSeconds.Value));
                            if (!success)
                                return;

                            LoadRequest loadRequest = BinaryStructConverter.FromByteArray<LoadRequest>(buffer);

                            this.Logger.LogMessage($"Load Request: LOAD\"{new string(loadRequest.FileName)}\",{loadRequest.DeviceNum}{(loadRequest.SecondaryAddr != 0 ? "," + loadRequest.SecondaryAddr : "")}");

                            LoadResponse loadResponse = storageAdapter.Load(loadRequest, floppyResolver, out byte[] payload);

                            ushort dest_ptr_start = 0x00;
                            if (payload != null)
                            {
                                byte[] dest_ptr_bytes = payload.Take(2).ToArray();
                                dest_ptr_start = (ushort)(payload[0] | (payload[1] << 8));
                                this.Logger.LogMessage($"Start Address: 0x{dest_ptr_start:X4}");

                                // check for known instant C64 crash addreses
                                // to help stabalize during testing
                                int end_dest_ptr = dest_ptr_start + payload.Length;

                                if (this.IsInvalidLoadAddress(dest_ptr_start, end_dest_ptr))
                                {
                                    payload = payload.Skip(2).ToArray(); // skip destination pointer    
                                    int endAddress = dest_ptr_start + payload.Length - 1;
                                    this.Logger.LogMessage($"End Address: 0x{endAddress:X4}");
                                }
                                else
                                {
                                    payload = null;
                                    loadResponse.ResponseCode = 0x04; // file not found
                                }
                            }

                            this.SendData(tcpClient, networkStream, loadResponse, dest_ptr_start, payload);

                        }
                        break;

                    case 0x02: // SAVE
                        {
                            int size = Marshal.SizeOf<SaveRequest>();

                            byte[] buffer = new byte[size];
                            buffer[0] = data[0];

                            success = this.ReadNetworkStream(networkStream, buffer, 1, size - 1, TimeSpan.FromSeconds(this.Configuration.ReceiveTimeoutSeconds.Value));
                            if (!success)
                                return;

                            SaveRequest saveRequest = BinaryStructConverter.FromByteArray<SaveRequest>(buffer);

                            this.Logger.LogMessage($"Save Request: SAVE\"{new string(saveRequest.FileName)}\",{saveRequest.DeviceNum}{(saveRequest.SecondaryAddr != 0 ? "," + saveRequest.SecondaryAddr : "")}");

                            // recv data
                            byte[] payload = this.ReceiveData(networkStream, saveRequest);

                            SaveResponse saveResponse = storageAdapter.Save(saveRequest, floppyResolver, payload);
                        }
                        break;

                    case 0x03: // INSERT/MOUNT floppy image
                        {
                            int size = Marshal.SizeOf<FloppyIdentifier>();

                            byte[] buffer = new byte[size];

                            this.ReadNetworkStream(networkStream, buffer, 0, size, TimeSpan.FromSeconds(this.Configuration.ReceiveTimeoutSeconds.Value));

                            FloppyIdentifier floppyIdentifier = BinaryStructConverter.FromByteArray<FloppyIdentifier>(buffer);
                            FloppyInfo floppyInfo = floppyResolver.InsertFloppy(floppyIdentifier);

                            // TODO: return response 
                        }
                        break;

                    case 0x04: // create floppy image
                        {
                            // not implemented yet
                            int size = Marshal.SizeOf<NewFloppyRequest>();

                            byte[] buffer = new byte[size];

                            this.ReadNetworkStream(networkStream, buffer, 0, size, TimeSpan.FromSeconds(this.Configuration.ReceiveTimeoutSeconds.Value));

                            NewFloppyRequest newFloppyRequest = BinaryStructConverter.FromByteArray<NewFloppyRequest>(buffer);
                            //  NewFloppyResponse newFloppyResponse = storageAdapter.CreateFloppy(newFloppyRequest);


                        }
                        break;

                    case 0x05: // search for floppys
                        {
                            int size = Marshal.SizeOf<SearchFloppiesRequest>();

                            byte[] buffer = new byte[size];
                            buffer[0] = data[0]; // operation byte - fix

                            this.ReadNetworkStream(networkStream, buffer, 1, size - 1, TimeSpan.FromSeconds(this.Configuration.ReceiveTimeoutSeconds.Value));

                            SearchFloppiesRequest searchFloppiesRequest = BinaryStructConverter.FromByteArray<SearchFloppiesRequest>(buffer);
                            SearchFloppyResponse searchFloppyResponse = floppyResolver.SearchFloppys(searchFloppiesRequest, out FloppyInfo[] foundFloppyInfos);

                            // build the payload
                            List<byte> payload = new List<byte>();
                            if (foundFloppyInfos != null)
                            {
                                foreach (FloppyInfo foundFloppyInfo in foundFloppyInfos)
                                {
                                    byte[] serializedFoundFloppyInfo = BinaryStructConverter.ToByteArray<FloppyInfo>(foundFloppyInfo);
                                    payload.AddRange(serializedFoundFloppyInfo);
                                }
                            }

                            int lengthOfPayload = payload.Count;
                            searchFloppyResponse.ByteCountLo = (byte)(lengthOfPayload & 0xFF); // LSB
                            searchFloppyResponse.ByteCountMid = (byte)((lengthOfPayload >> 8) & 0xFF);
                            searchFloppyResponse.ByteCountHi = (byte)((lengthOfPayload >> 16) & 0xFF); // MSB

                            this.SendData(tcpClient, networkStream, searchFloppyResponse, (ushort)(searchFloppyResponse.DestPtrHi << 8 | searchFloppyResponse.DestPtrLo), payload.ToArray());
                        }
                        break;
                }

                networkStream.Flush();
            }
        }

        private bool IsInvalidLoadAddress(ushort dest_ptr_start, int end_dest_ptr)
        {
            // TODO: for now returning a file not found
            // but need to investigate better handling later
            List<ushort> rejectedLoadAddresses = new List<ushort>()
                                {
                                    0x0314, // BASIC IRQ
                                    0x0316, // BASIC NMI
                                    0xFFFE, // KERNAL IRQ
                                    0xFFFA, // KERNAL NMI
                                    0xEA38, // LOAD address IRQ
                                    0xC000  // VDRIVE location
                                };

            if (rejectedLoadAddresses.Any(r => r == dest_ptr_start)
                || end_dest_ptr >= 0xc000)
            {
                this.Logger.LogMessage($"Warning: invalid load address or end address 0x{dest_ptr_start:X4}-0x{end_dest_ptr:X4}, rejecting load to prevent C64 lockup");
                return false;
            }

            return true;

        }

        private bool ReadNetworkStream(NetworkStream stream, byte[] buffer, int offset, int count, TimeSpan timeout)
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
                   // this.Logger.LogMessage($"Received: {r} bytes ", LogSeverity.Info); // debug SAVE 
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

        private byte[] ReceiveData(NetworkStream networkStream, SaveRequest saveRequest)
        {
            ushort chunkSize = (ushort)((saveRequest.ChunkSizeHi << 8) | saveRequest.ChunkSizeLo);
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
                    this.Logger.LogMessage("Timeout waiting for initial sync byte, aborting receive", LogSeverity.Error);
                    throw new TimeoutException("Timed out waiting for sync byte");
                }

                if (!networkStream.DataAvailable)
                {
                    Thread.Sleep(10);
                    continue;
                }

                bool sucess = ReadNetworkStream(networkStream, oneByte, 0, 1, timeout);
                if (sucess && oneByte[0] == 0x2B) break; // sync found
                                                         // else drop garbage and continue
            }

            int chunksReceived = 0;
            while (received < totalSize)
            {
                receiveTimeoutStopWatch.Restart();

                int toRead = Math.Min(chunkSize, totalSize - received);
                bool success = ReadNetworkStream(networkStream, buffer, received, toRead, timeout);
                if (!success)
                {
                    this.Logger.LogMessage($"Timeout or read error receiving chunk at offset {received}, aborting receive", LogSeverity.Error);
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

                int startByte = received;
                int endByte = received + chunkSize;

                this.Logger.LogMessage($"Received {startByte}-{endByte} bytes (chunk {chunksReceived} of {chunkSize})");
            }

            return buffer;
        }

        private void SendData<T>(TcpClient tcpClient, NetworkStream networkStream, T header, ushort dest_address_start, byte[] payload) where T : struct
        {
            DateTime start = DateTime.Now;

            byte[] sendBuffer = new byte[1];

            // sync byte
            sendBuffer[0] = (byte)'+';
            networkStream.Write(sendBuffer, 0, 1);

            byte[] headerBytes = BinaryStructConverter.ToByteArray<T>(header);
            networkStream.Write(headerBytes, 0, headerBytes.Length);

            if (payload == null) // file not found or no payload (save response)
            {
                return;
            }

            this.SendChunks(tcpClient, networkStream, dest_address_start, payload, this.Configuration.ChunkSize, TimeSpan.FromSeconds(this.Configuration.SendTimeoutSeconds.Value));

            DateTime end = DateTime.Now;
            this.Logger.LogMessage($"TimeTook:{((end - start).TotalMilliseconds / 1000).ToString("F3")} " + $"BytesPerSec:{(payload.Length / (end - start).TotalMilliseconds * 1000).ToString("F3")}"
);

            networkStream.Flush();
        }

        private void SendChunks(TcpClient tcpClient, NetworkStream networkStream, ushort dest_addre_start, byte[] payload, ushort chunkSize, TimeSpan timeout)
        {
            IList<List<byte>> chunks = payload.ToList().BuildChunks(chunkSize).ToList();
            int numOfChunks = chunks.Count();
            int bytesSent = 0;

            for (int chunkIndex = 0; chunkIndex < numOfChunks; chunkIndex++)
            {
                List<byte> chunk = chunks[chunkIndex];

                // FIXME
                ushort baseAddress = dest_addre_start;
                int startByte = bytesSent;
                int endByte = bytesSent + chunk.Count;

                int startAddress = dest_addre_start + bytesSent;
                int endAddress = startAddress + chunk.Count - 1;

                this.Logger.LogMessage($"Sending {startByte}-{endByte} bytes to 0x{startAddress:X4}-0x{endAddress:X4} (chunk {chunkIndex + 1} of {chunks.Count})");

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

                        case 0x03: // cancel sending
                            this.Logger.LogMessage("Send canceled");
                            return;
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
