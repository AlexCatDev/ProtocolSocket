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
        //The maximum size of a single receive.
        public const int BUFFER_SIZE = 8192;
        //The size of the size header.
        public const int SIZE_BUFFER_LENGTH = 4;

        public delegate void PacketReceivedEventHandler(ProtocolSocket sender, PacketReceivedEventArgs PacketReceivedEventArgs);
        public delegate void DOSDetectedEventHandler(ProtocolSocket sender);
        public delegate void ClientStateChangedEventHandler(ProtocolSocket sender, Exception Message, bool Connected);
        public delegate void ReceiveProgressChangedEventHandler(ProtocolSocket sender, int Received, int BytesToReceive);
        public delegate void SendProgressChangedEventHandler(ProtocolSocket sender, int Send);

        public event ReceiveProgressChangedEventHandler ReceiveProgressChanged;
        public event PacketReceivedEventHandler PacketReceived;
        public event ClientStateChangedEventHandler ClientStateChanged;
        public event DOSDetectedEventHandler DOSDetected;
        public event SendProgressChangedEventHandler SendProgressChanged;

        public object UserToken { get; set; }
        public bool Running { get; private set; }

        public EndPoint RemoteEndPoint { get; private set; }
        public ProtocolSocketOptions SocketOptions { get; }

        private Socket socket;
        private Stopwatch stopWatch;
        private object syncLock;

        private byte[] buffer;
        private int payloadSize;
        private int totalPayloadSize;
        private MemoryStream payloadStream;

        int receiveRate = 0;

        public ProtocolSocket(Socket socket, ProtocolSocketOptions socketOptions)
        {
            if (socket != null) {
                this.socket = socket;
                SocketOptions = socketOptions;
                RemoteEndPoint = socket.RemoteEndPoint;

                Setup();
            } else {
                throw new InvalidOperationException("Socket is null");
            }
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

            InitBuffer();
        }

        public void Start()
        {
            if (!Running) {
                Running = true;
                ClientStateChanged?.Invoke(this, null, Running);
                stopWatch = Stopwatch.StartNew();

                BeginRead();

            } else if (Running) {
                throw new InvalidOperationException("Client already running");
            }
        }

        private void InitBuffer()
        {
            //Create a new instance of a byte array based on the buffer size
            buffer = new byte[BUFFER_SIZE];
        }

        public void Disconnect(bool reuseSocket)
        {
            HandleDisconnect(new Exception("Manual disconnect"), reuseSocket);
        }

        public void Connect(string IP, int Port)
        {
            try {
                socket.Connect(IP, Port);
                Start();
            } catch {
                throw;
            }
        }

        private void BeginRead()
        {
            try {
                //Attempt to receive the buffer size
                socket.BeginReceive(buffer, 0, SIZE_BUFFER_LENGTH, 0,
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

                /*An exception should be thrown on EndReceive if the connection was lost.
                However, that is not always the case, so we check if read is zero or less.
                Which means disconnection.
                If there is a disconnection, we throw an exception*/
                if (read <= 0) {
                    throw new SocketException((int)SocketError.ConnectionAborted);
                }

                //If we didn't receive the full buffer size, something is lagging behind.
                if (read < SIZE_BUFFER_LENGTH) {
                    //Calculate how much is missing.
                    var left = SIZE_BUFFER_LENGTH - read;

                    //Wait until there is at least that much avilable, and sleep a little while(Might aswell do that since the connection is slow,
                    //So it won't hurt performance)
                    while (socket.Available < left) {
                        Thread.Sleep(30);
                    }

                    //Use the synchronous receive since the data is close behind and shouldn't take much time.
                    socket.Receive(buffer, read, left, 0);
                }

                //Get the converted int value for the payload size from the received data
                payloadSize = BitConverter.ToInt32(buffer, 0);
                totalPayloadSize = payloadSize;

                    if (payloadSize > SocketOptions.MaxPacketSize)
                        throw new ProtocolViolationException($"Payload size exeeded max allowed [{payloadSize} > {SocketOptions.MaxPacketSize}]");
                    else if (payloadSize == 0) {
                        if (SocketOptions.AllowZeroLengthPackets) {
                            BeginRead();
                            PacketReceived?.Invoke(this, new PacketReceivedEventArgs(new byte[0]));
                        } else
                            throw new ProtocolViolationException("Zero-length packets are now set to be allowed!");
                    }

                /*Get the initialize size we will read
                 * If its not more than the buffer size, we'll just use the full length*/
                var initialSize = payloadSize > BUFFER_SIZE ? BUFFER_SIZE :
                    payloadSize;

                //Initialize a new MemStream to receive chunks of the payload
                this.payloadStream = new MemoryStream();

                //Start the receive loop of the payload
                socket.BeginReceive(buffer, 0, initialSize, 0,
                    ReceivePayloadCallBack, null);
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

                //Subtract what we read from the payload size.
                payloadSize -= read;

                //Write the data to the payload stream.
                payloadStream.Write(buffer, 0, read);

                ReceiveProgressChanged?.Invoke(this, (int)payloadStream.Length, totalPayloadSize);

                //If there is more data to receive, keep the loop going.
                if (payloadSize > 0) {
                    //See how much data we need to receive like the initial receive.
                    int receiveSize = payloadSize > BUFFER_SIZE ? BUFFER_SIZE :
                        payloadSize;
                    socket.BeginReceive(buffer, 0, receiveSize, 0,
                        ReceivePayloadCallBack, null);
                } else //If we received everything
                  {
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
            } catch (NullReferenceException) { return; } catch (ObjectDisposedException) { return; } catch (Exception ex) {
                HandleDisconnect(ex, false);
            }
        }

        private void CheckFlood()
        {
            if (SocketOptions.DOSProtection != null) {
                receiveRate++;

                //Time to check for receive rate
                if (stopWatch.ElapsedMilliseconds >= SocketOptions.DOSProtection.OverTime) {

                    //Check if we exeeded the maximum receive rate
                    if (receiveRate > SocketOptions.DOSProtection?.MaxPackets)
                        DOSDetected?.Invoke(this);

                    receiveRate = 0;
                    stopWatch.Restart();
                }
            }
        }

        private void HandleDisconnect(Exception ex, bool reuseSocket)
        {
            Running = false;
            socket.Disconnect(reuseSocket);
            if (ex != null)
                ClientStateChanged?.Invoke(this, ex, Running);
            else
                ClientStateChanged?.Invoke(this, new Exception("None"), Running);
        }

        public void UnsubscribeEvents()
        {
            PacketReceived = null;
            DOSDetected = null;
            ClientStateChanged = null;
            SendProgressChanged = null;
            ReceiveProgressChanged = null;
        }

        private void SendData(byte[] data)
        {
            lock (syncLock) {
                socket.Send(BitConverter.GetBytes(data.Length));
                socket.Send(data);
                SendProgressChanged?.Invoke(this, data.Length + SIZE_BUFFER_LENGTH);
            }
        }

        public void SendPacket(byte[] bytes)
        {
            SendData(bytes);
        }

        public void Close()
        {
            socket.Close();
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
    }
}
