using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace ProtocolSocket
{
    public class ProtocolSocket : IDisposable
    {
        //The size of the size header.
        public const int SIZE_BUFFER_LENGTH = 4;

        public delegate void PacketReceivedEventHandler(ProtocolSocket sender, PacketReceivedEventArgs packetReceivedEventArgs);
        public delegate void DOSDetectedEventHandler(ProtocolSocket sender);
        public delegate void ReceiveProgressChangedEventHandler(ProtocolSocket sender, int bytesReceived, int bytesToReceive);
        public delegate void SendProgressChangedEventHandler(ProtocolSocket sender, int send);
        public delegate void ConnectionEstablishedEventHandler(ProtocolSocket sender);
        public delegate void ConnectionErrorEventHandler(ProtocolSocket sender, Exception exception);

        public event ConnectionEstablishedEventHandler ConnectionEstablished;
        public event ConnectionErrorEventHandler ConnectionError;
        public event PacketReceivedEventHandler PacketReceived;
        public event DOSDetectedEventHandler DOSDetected;
        public event ReceiveProgressChangedEventHandler ReceiveProgressChanged;
        public event SendProgressChangedEventHandler SendProgressChanged;

        public object UserToken { get; set; }
        public bool Running { get; private set; }

        public EndPoint RemoteEndPoint { get; private set; }
        public ProtocolSocketOptions SocketOptions { get; }

        private Socket socket;
        private Stopwatch stopWatch;
        private object syncLock;

        private byte[] buffer;
        private int totalRead;
        private int payloadSize;
        private int totalPayloadSize;
        private bool useBuffer;
        private MemoryStream payloadStream;

        private int receiveRate;

        public ProtocolSocket(Socket socket, ProtocolSocketOptions socketOptions) {
            this.socket = socket;
            SocketOptions = socketOptions;
            RemoteEndPoint = socket.RemoteEndPoint;

            Setup();
        }

        public ProtocolSocket(ProtocolSocketOptions socketOptions)
        {
            SocketOptions = socketOptions;
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.NoDelay = true;

            Setup();
        }

        private void Setup()
        {
            Running = false;
            syncLock = new object();
            receiveRate = 0;
            totalRead = 0;

            InitBuffer();
        }

        public void Start()
        {
            if (socket != null && SocketOptions != null) {
                if (!Running) {
                    Running = true;
                    ConnectionEstablished?.Invoke(this);
                    stopWatch = Stopwatch.StartNew();

                    BeginRead();
                }
                else if (Running) {
                    throw new InvalidOperationException("Client is already running");
                }
            }
            else {
                throw new InvalidOperationException("Socket or SocketOptions can't be null");
            }
        }

        private void InitBuffer()
        {
            //Create a new instance of a byte array based on the buffer size
            buffer = new byte[SocketOptions.ReceiveBufferSize];
        }

        public void Disconnect(bool reuseSocket)
        {
            HandleDisconnect(new Exception("Manual disconnect"), reuseSocket);
        }

        [Obsolete("Please use ConnectAsync() method instead")]
        public void Connect(string IP, int Port)
        {
            try {
                socket.Connect(IP, Port);
                Start();
            } catch {
                throw;
            }
        }

        public void ConnectAsync(string host, int port) {
            socket.BeginConnect(host, port, ConnectCallBack, null);
        }

        public void ConnectAsync(EndPoint remoteEP) {
            socket.BeginConnect(remoteEP, ConnectCallBack, null);
        }

        private void ConnectCallBack(IAsyncResult ar) {
            try {
                socket.EndConnect(ar);
                Start();
            }
            catch(Exception ex) { ConnectionError?.Invoke(this, ex); }
        }

        private void BeginRead()
        {
            try {
                //Attempt to receive the buffer size
                socket.BeginReceive(buffer, totalRead, SIZE_BUFFER_LENGTH - totalRead, 0,
                    ReadSizeCallBack, null);
            } catch (NullReferenceException) { return; } catch (ObjectDisposedException) { return; } catch (Exception ex) {
                HandleDisconnect(ex, false);
            }
        }

        /// <summary>
        /// This callback is for handling the receive of the size header.
        /// </summary>
        /// <param name="ar"></param>
        private void ReadSizeCallBack(IAsyncResult ar)
        {
            try {
                //Attempt to end the read
                var read = socket.EndReceive(ar);
                //Total data read
                totalRead += read;

                /*An exception should be thrown on EndReceive if the connection was lost.
                However, that is not always the case, so we check if read is zero or less.
                Which means disconnection.
                If there is a disconnection, we throw an exception*/
                if (read <= 0) {
                    throw new SocketException((int)SocketError.ConnectionAborted);
                }

                //If we didn't receive the full buffer size, something is lagging behind.
                if (totalRead < SIZE_BUFFER_LENGTH) {
                    //Begin read again
                    BeginRead();

                    //If we did receive the full buffer size, then begin reading the payload
                } else {
                    totalRead = 0;

                    //Get the converted int value for the payload size from the received data
                    payloadSize = BitConverter.ToInt32(buffer, 0);
                    totalPayloadSize = payloadSize;

                    if (payloadSize > SocketOptions.MaxPacketSize)
                        throw new ProtocolViolationException($"Payload size exeeded max allowed [{payloadSize} > {SocketOptions.MaxPacketSize}]");
                    else if (payloadSize == 0) {
                        if (SocketOptions.AllowZeroLengthPackets) {
                            BeginRead();
                            PacketReceived?.Invoke(this, new PacketReceivedEventArgs(new byte[0]));
                        }
                        else
                            throw new ProtocolViolationException("Zero-length packets are now set to be allowed!");
                    }
                    else {
                        /*Get the initialize size we will read
                         * If its not more than the buffer size, we'll just use the full length*/
                        var initialSize = payloadSize > SocketOptions.ReceiveBufferSize ? SocketOptions.ReceiveBufferSize :
                            payloadSize;

                        if (initialSize < SocketOptions.ReceiveBufferSize)
                            useBuffer = true;

                        //Initialize a new MemStream to receive chunks of the payload
                        if (!useBuffer) {
                            payloadStream = new MemoryStream();

                            //Start the receive loop of the payload
                            socket.BeginReceive(buffer, 0, initialSize, 0,
                                ReceivePayloadCallBack, null);
                        }
                        else {
                            socket.BeginReceive(buffer, totalRead, totalPayloadSize - totalRead, 
                                0, ReceivePayloadCallBack, null);
                        }
                    }
                }
            } catch (NullReferenceException) { return; } catch (ObjectDisposedException) { return; } catch (Exception ex) {
                HandleDisconnect(ex, false);
            }
        }

        private void ReceivePayloadCallBack(IAsyncResult ar)
        {
            try {
                //Attempt to finish the async read.
                var read = socket.EndReceive(ar);

                //Same as above
                if (read <= 0) {
                    throw new SocketException((int)SocketError.ConnectionAborted);
                }

                CheckFlood();

                if (!useBuffer) {
                    //Subtract what we read from the payload size.
                    payloadSize -= read;

                    //Write the data to the payload stream.
                    payloadStream.Write(buffer, 0, read);

                    ReceiveProgressChanged?.Invoke(this, (int)payloadStream.Length, totalPayloadSize);

                    //If there is more data to receive, keep the loop going.
                    if (payloadSize > 0) {
                        //See how much data we need to receive like the initial receive.
                        int receiveSize = payloadSize > SocketOptions.ReceiveBufferSize ? SocketOptions.ReceiveBufferSize :
                            payloadSize;
                        socket.BeginReceive(buffer, 0, receiveSize, 0,
                            ReceivePayloadCallBack, null);
                    }
                    else //If we received everything
                    {
                        ReceiveProgressChanged?.Invoke(this, (int)payloadStream.Length, totalPayloadSize);

                        //Close the payload stream
                        payloadStream.Close();

                        //Get the full payload
                        byte[] payload = payloadStream.ToArray();

                        //Dispose the stream
                        payloadStream = null;

                        //Start reading
                        BeginRead();
                        //Call the event method
                        PacketReceived?.Invoke(this, new PacketReceivedEventArgs(payload));
                    }
                }
                else {
                    if(totalRead < totalPayloadSize) {
                        socket.BeginReceive(buffer, totalRead, totalPayloadSize - totalRead, 0,
                            ReceivePayloadCallBack, null);

                        ReceiveProgressChanged?.Invoke(this, totalRead, totalPayloadSize);
                    }
                    else {
                        ReceiveProgressChanged?.Invoke(this, totalRead, totalPayloadSize);
                        PacketReceived?.Invoke(this, new PacketReceivedEventArgs(buffer));
                        BeginRead();
                    }
                }
            } catch (NullReferenceException) { return; } catch (ObjectDisposedException) { return; } catch (Exception ex) {
                HandleDisconnect(ex, false);
            }
        }

        private void CheckFlood()
        {
            if (SocketOptions.DOSProtection != null) {
                receiveRate++;

                //Time to check for receive rate
                if (stopWatch.ElapsedMilliseconds >= SocketOptions.DOSProtection.Delta) {

                    //Check if we exeeded the maximum receive rate
                    if (receiveRate > SocketOptions.DOSProtection?.MaxPackets)
                        DOSDetected?.Invoke(this);

                    receiveRate = 0;
                    stopWatch.Restart();
                }
            }
        }

        public void SendPacket(byte[] packet)
        {
            lock (syncLock) {
                socket.Send(BitConverter.GetBytes(packet.Length));
                socket.Send(packet);
                SendProgressChanged?.Invoke(this, packet.Length + sizeof(int));
            }
        }

        public void SendPacketCombined(byte[] packet) {
            byte[] lengthData = BitConverter.GetBytes(packet.Length);
            byte[] combinedPacket = Combine(lengthData, packet);
            socket.Send(combinedPacket);
            SendProgressChanged?.Invoke(this, combinedPacket.Length);
        }

        public void SendPacketAsync(byte[] packet) {
            byte[] lengthData = BitConverter.GetBytes(packet.Length);
            byte[] combinedPacket = Combine(lengthData, packet);
            socket.BeginSend(combinedPacket, 0, combinedPacket.Length, 0, SendPacketCallBack, null);
        }

        private void SendPacketCallBack(IAsyncResult ar) {
            int send = socket.EndSend(ar);

            SendProgressChanged?.Invoke(this, send);
        }

        private void HandleDisconnect(Exception ex, bool reuseSocket) {
            Running = false;
            socket.Disconnect(reuseSocket);
            ConnectionError?.Invoke(this, ex);
        }

        public void Close()
        {
            socket.Close();
        }

        public void UnsubscribeEvents() {
            PacketReceived = null;
            DOSDetected = null;
            ConnectionEstablished = null;
            ConnectionError = null;
            SendProgressChanged = null;
            ReceiveProgressChanged = null;
        }

        public void Dispose()
        {
            Close();
            buffer = null;
            RemoteEndPoint = null;
            //TODO: Disposal of socket options
            UserToken = null;
            payloadStream?.Dispose();

            UnsubscribeEvents();
        }

        public static byte[] Combine(byte[] first, byte[] second) {
            byte[] ret = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, ret, 0, first.Length);
            Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
            return ret;
        }
    }
}
