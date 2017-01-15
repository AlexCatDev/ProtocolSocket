using System;
using System.Collections.Generic;
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
        public const int SIZE_HEADER = sizeof(int);

        public delegate void PacketReceivedEventHandler(ProtocolSocket sender, PacketReceivedEventArgs packetReceivedEventArgs);
        public delegate void DOSDetectedEventHandler(ProtocolSocket sender);
        public delegate void ReceiveProgressChangedEventHandler(ProtocolSocket sender, int bytesReceived, int bytesToReceive);
        public delegate void PacketSendEventHandler(ProtocolSocket sender, int bytesSend);
        public delegate void ConnectionEstablishedEventHandler(ProtocolSocket sender);
        public delegate void ConnectionErrorEventHandler(ProtocolSocket sender, Exception exception);

        public event ConnectionEstablishedEventHandler ConnectionEstablished;
        public event ConnectionErrorEventHandler ConnectionError;
        public event PacketReceivedEventHandler PacketReceived;
        public event PacketSendEventHandler PacketSend;
        public event DOSDetectedEventHandler DOSDetected;
        public event ReceiveProgressChangedEventHandler ReceiveProgressChanged;

        public object UserToken { get; set; }
        public bool Running { get; private set; }

        public EndPoint RemoteEndPoint { get; private set; }
        public ProtocolSocketOptions SocketOptions { get; private set; }

        private Socket socket;
        private Stopwatch stopWatch;
        private object syncLock;
        private Queue<byte[]> sendQueue;

        private byte[] buffer;
        private int dataRead;
        private int dataExpected;
        private MemoryStream payloadStream;

        private int receiveRate;

        public ProtocolSocket(Socket socket, ProtocolSocketOptions socketOptions) {
            this.socket = socket;
            SocketOptions = socketOptions;
            RemoteEndPoint = socket.RemoteEndPoint;

            Setup();
        }

        public ProtocolSocket(ProtocolSocketOptions socketOptions) {
            SocketOptions = socketOptions;
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.NoDelay = true;

            Setup();
        }

        private void Setup() {
            syncLock = new object();
            sendQueue = new Queue<byte[]>();
            receiveRate = 0;
            dataRead = 0;
            buffer = new byte[SocketOptions.BufferSize];
        }

        public void Start() {
            if (socket != null && SocketOptions != null) {
                if (!Running) {
                    Running = true;
                    ConnectionEstablished?.Invoke(this);
                    stopWatch = Stopwatch.StartNew();

                    BeginReceive();
                }
                else if (Running) {
                    throw new InvalidOperationException("Client is already running");
                }
            }
            else {
                throw new InvalidOperationException("Socket or SocketOptions can't be null");
            }
        }

        public void Disconnect(bool reuseSocket) {
            HandleDisconnect(new Exception("Manual disconnect"), reuseSocket);
        }

        [Obsolete("Please use ConnectAsync() method instead")]
        public void Connect(string IP, int Port) {
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

        public void ConnectAsync(EndPoint hostEP) {
            socket.BeginConnect(hostEP, ConnectCallBack, null);
        }

        private void ConnectCallBack(IAsyncResult ar) {
            try {
                socket.EndConnect(ar);
                Start();
            }
            catch(Exception ex) { ConnectionError?.Invoke(this, ex); }
        }

        private void BeginReceive() {
            //First we begin receive the size header
            socket.BeginReceive(buffer, dataRead, SIZE_HEADER - dataRead, 0,
                ReceiveSizeCallback, null);
        }

        /// <summary>
        /// This callback is for handling the receive of the size header.
        /// </summary>
        /// <param name="ar"></param>
        private void ReceiveSizeCallback(IAsyncResult ar) {
            try {
                //Attempt to end the read
                var read = socket.EndReceive(ar);

                /*An exception should be thrown on EndReceive if the connection was lost.
                However, that is not always the case, so we check if read is zero or less.
                Which means disconnection.
                If there is a disconnection, we throw an exception*/
                if (read <= 0)
                    throw new SocketException((int)SocketError.ConnectionAborted);

                //increment the total data read
                dataRead += read;

                //If we didn't receive the full buffer size, something is lagging behind.
                if (dataRead < SIZE_HEADER) {
                    //Begin receive again
                    BeginReceive();

                    //If we did receive the full header, then begin reading the payload
                } else {
                    dataRead = 0;

                    //Get the converted int value for the payload size from the received data
                    dataExpected = BitConverter.ToInt32(buffer, 0);

                    if (dataExpected > SocketOptions.MaxPacketSize)
                        throw new ProtocolViolationException($"Payload size exeeded whats max allowed {dataExpected} > {SocketOptions.MaxPacketSize}");
                    else if (dataExpected == 0)
                            throw new ProtocolViolationException("Data with no length is not allowed");
                    else {
                        /*If the expected data size is bigger than what
                         * we can hold we need to write it to a resizable stream */
                        if (dataExpected > buffer.Length) {

                            //Initialize a new MemStream to receive chunks of the payload
                            payloadStream = new MemoryStream();

                            //Start the receive loop of the payload
                            socket.BeginReceive(buffer, 0, buffer.Length, 0,
                                ReceivePayloadStreamCallback, null);
                        } else {
                            //Else we receive directly into the buffer
                            socket.BeginReceive(buffer, 0, dataExpected, 0,
                                ReceivePayloadBufferedCallback, null);
                        }
                    }
                }
            } catch (NullReferenceException) {
                return;
            } catch (ObjectDisposedException) {
                return;
            } catch (Exception ex) {
                HandleDisconnect(ex, false);
            }
        }

        private void ReceivePayloadBufferedCallback(IAsyncResult ar) {
            try {
                //Attempt to finish the async read.
                var read = socket.EndReceive(ar);

                //Same as above
                if (read <= 0)
                    throw new SocketException((int)SocketError.ConnectionAborted);

                CheckFlood();

                //Increment how much data we have read
                dataRead += read;
                Console.WriteLine(dataRead);
                ReceiveProgressChanged?.Invoke(this, dataRead, dataExpected);

                //If there is more data to receive, keep the loop going.
                if (dataRead < dataExpected) {
                    socket.BeginReceive(buffer, dataRead, dataExpected - dataRead, 0,
                                ReceivePayloadBufferedCallback, null);

                    //If we received everything
                } else {

                    //Reset dataRead for receiving size
                    dataRead = 0;

                    //Call the event method
                    PacketReceived?.Invoke(this, new PacketReceivedEventArgs(buffer));

                    //Start receiving the size again
                    BeginReceive();
                }
            } catch (NullReferenceException) {
                return;
            } catch (ObjectDisposedException) {
                return;
            } catch (Exception ex) {
                HandleDisconnect(ex, false);
            }
        }

        private void ReceivePayloadStreamCallback(IAsyncResult ar) {
            try {
                //Attempt to finish the async read.
                var read = socket.EndReceive(ar);

                //Same as above
                if (read <= 0)
                    throw new SocketException((int)SocketError.ConnectionAborted);

                CheckFlood();
                //Subtract what we read from the payload size.
                dataExpected -= read;

                //Write the data to the payload stream.
                payloadStream.Write(buffer, 0, read);

                ReceiveProgressChanged?.Invoke(this, (int)payloadStream.Length, dataExpected);

                //If there is more data to receive, keep the loop going.
                if (dataExpected > 0) {
                    //See how much data we need to receive like the initial receive.
                    int receiveSize = dataExpected > SocketOptions.BufferSize ?
                        SocketOptions.BufferSize : dataExpected;

                    socket.BeginReceive(buffer, 0, receiveSize, 0,
                        ReceivePayloadStreamCallback, null);
                    //If we received everything
                } else {
                    //Close the payload stream
                    payloadStream.Close();

                    //Get the full payload
                    byte[] payload = payloadStream.ToArray();

                    //Dispose the stream
                    payloadStream = null;

                    //Start receiving size header again
                    BeginReceive();

                    //Call the event method
                    PacketReceived?.Invoke(this, new PacketReceivedEventArgs(payload));
                }
            } catch (NullReferenceException) {
                return;
            } catch (ObjectDisposedException) {
                return;
            } catch (Exception ex) {
                HandleDisconnect(ex, false);
            }
        }

        private void CheckFlood() {
            if (SocketOptions.DOSProtection != null) {
                receiveRate++;

                //Time to check for receive rate
                if (stopWatch.ElapsedMilliseconds >= SocketOptions.DOSProtection.Delta) {

                    //Check if we exeeded the maximum receive rate
                    if (receiveRate > SocketOptions.DOSProtection.MaxPackets)
                        DOSDetected?.Invoke(this);

                    receiveRate = 0;
                    stopWatch.Restart();
                }
            }
        }

        public void Send(byte[] packet) {
            lock (syncLock) {
                socket.Send(BitConverter.GetBytes(packet.Length));
                socket.Send(packet);
                PacketSend?.Invoke(this, packet.Length + sizeof(int));
            }
        }

        public void SendAsync(byte[] packet) {
            ThreadPool.QueueUserWorkItem((o) => {
                Send(packet);
                packet = null;
            });
        }

        private void HandleDisconnect(Exception ex, bool reuseSocket) {
            Running = false;
            socket.Disconnect(reuseSocket);
            ConnectionError?.Invoke(this, ex);
        }

        public void Close() {
            socket.Close();
        }

        public void UnsubscribeEvents() {
            PacketReceived = null;
            DOSDetected = null;
            ConnectionEstablished = null;
            ConnectionError = null;
            PacketSend = null;
            ReceiveProgressChanged = null;
        }

        public void Dispose() {
            Close();
            buffer = null;
            RemoteEndPoint = null;
            SocketOptions = null;
            payloadStream?.Dispose();
            sendQueue.Clear();

            if (UserToken is IDisposable)
                (UserToken as IDisposable).Dispose();
            else
                UserToken = null;

            UnsubscribeEvents();
        }

        public void SetInternalSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, int optionValue) {
            socket.SetSocketOption(optionLevel, optionName, optionValue);
        }

        public void SetInternalSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, bool optionValue) {
            socket.SetSocketOption(optionLevel, optionName, optionValue);
        }

        public void SetInternalSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, object optionValue) {
            socket.SetSocketOption(optionLevel, optionName, optionValue);
        }

        public void SetInternalSocketOption(SocketOptionLevel optionLevel, SocketOptionName optionName, byte[] optionValue) {
            socket.SetSocketOption(optionLevel, optionName, optionValue);
        }
    }
}
