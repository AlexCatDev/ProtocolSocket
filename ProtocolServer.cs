using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace ProtocolSocket
{
    public class ProtocolServer : IDisposable
    {
        //Listener event delegates
        public delegate void ListeningStateChangedEventHandler(ProtocolServer sender, bool listening);

        //Listener events
        public event ListeningStateChangedEventHandler ListeningStateChanged;

        //Client event delegates
        public delegate void PacketReceivedEventHandler(ProtocolSocket sender, PacketReceivedEventArgs packetReceivedEventArgs);
        public delegate void DOSDetectedEventHandler(ProtocolSocket sender);
        public delegate void ClientStateChangedEventHandler(ProtocolSocket sender, Exception message, bool connected);
        public delegate void ReceiveProgressChangedEventHandler(ProtocolSocket sender, int received, int bytesToReceive);
        public delegate void PacketSendEventHandler(ProtocolSocket sender, int bytesSend);
        public delegate void ConnectionEstablishedEventHandler(ProtocolSocket sender);
        public delegate void ConnectionErrorEventHandler(ProtocolSocket sender, Exception exception);

        //Client events
        public event ConnectionEstablishedEventHandler ConnectionEstablished;
        public event ConnectionErrorEventHandler ConnectionError;
        public event ReceiveProgressChangedEventHandler ReceiveProgressChanged;
        public event PacketReceivedEventHandler PacketReceived;
        public event DOSDetectedEventHandler DOSDetected;
        public event PacketSendEventHandler PacketSend;

        public bool Listening { get; private set; }
        public ProtocolServerOptions ServerSocketOptions { get; private set; }

        public IList<ProtocolSocket> ConnectedClients {
            get {
                lock (syncLock) {
                    return connectedClients.AsReadOnly();
                }
            }
        }

        public IList<string> BlockedHosts {
            get {
                return blockedHosts.AsReadOnly();
            }
        }

        Socket listeningSocket;
        List<ProtocolSocket> connectedClients;
        List<string> blockedHosts;
        object syncLock;

        public ProtocolServer(ProtocolServerOptions serverSocketOptions) {
            Listening = false;
            syncLock = new object();
            ServerSocketOptions = serverSocketOptions;
            connectedClients = new List<ProtocolSocket>();
            blockedHosts = new List<string>();
        }

        public void Listen() {
            if (Listening) {
                throw new InvalidOperationException("Server is already listening. Please call Close() before calling Listen()");
            } else {
                if (ServerSocketOptions.ListenEndPoint == null)
                    throw new InvalidOperationException("Please specify a endpoint to listen on before calling Listen()");

                listeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                listeningSocket.Bind(ServerSocketOptions.ListenEndPoint);
                listeningSocket.Listen(ServerSocketOptions.MaxConnectionQueue);
                Listening = true;
                ListeningStateChanged?.Invoke(this, Listening);
                listeningSocket.BeginAccept(AcceptCallBack, null);
            }
        }

        public void Stop() {
            if (Listening) {
                listeningSocket.Close();
                listeningSocket = null;
                foreach (var client in connectedClients) {
                    client.Dispose();
                }
                connectedClients.Clear();
                Listening = false;
                ListeningStateChanged?.Invoke(this, Listening);
            } else {
                throw new InvalidOperationException("Server isn't listening. Please call Listen() before calling Close()");
            }
        }

        private void AcceptCallBack(IAsyncResult iar) {
            try {
                Socket accepted = listeningSocket.EndAccept(iar);
                if (blockedHosts.Contains((accepted.RemoteEndPoint as IPEndPoint).Address.ToString())) {
                    accepted.Disconnect(false);
                    return;
                }
                accepted.NoDelay = true;
                ProtocolSocket protocolSocket = new ProtocolSocket(accepted, ServerSocketOptions.ProtocolSocketOptions);

                protocolSocket.ConnectionError += ProtocolSocket_ConnectionError;
                protocolSocket.ConnectionEstablished += ProtocolSocket_ConnectionEstablished;
                protocolSocket.PacketReceived += ProtocolSocket_PacketReceived;
                protocolSocket.ReceiveProgressChanged += ProtocolSocket_ReceiveProgressChanged;
                protocolSocket.PacketSend += ProtocolSocket_PacketSend;
                protocolSocket.DOSDetected += ProtocolSocket_DOSDetected;

                protocolSocket.Start();
                listeningSocket.BeginAccept(AcceptCallBack, null);
            } catch {
                //MessageBox.Show(ex.Message + " \n\n [" + ex.StackTrace + "]");
            }
        }

        private void ProtocolSocket_ConnectionEstablished(ProtocolSocket sender) {
            lock (syncLock) {
                connectedClients.Add(sender);
                ConnectionEstablished?.Invoke(sender);
            }
        }

        private void ProtocolSocket_ConnectionError(ProtocolSocket sender, Exception exception) {
            lock(syncLock) {
                connectedClients.Remove(sender);
                ConnectionError?.Invoke(sender, exception);
            }
        }

        public void Broadcast(byte[] packet, ProtocolSocket exception = null) {
            lock (syncLock) {
                for (int i = 0; i > connectedClients.Count; i++) {
                    var client = connectedClients[i];
                    if (client != exception) {
                        try {
                            client.Send(packet);
                        } catch { }
                    }
                }
            }
        }

        private void ProtocolSocket_PacketSend(ProtocolSocket sender, int Send) {
            PacketSend?.Invoke(sender, Send);
        }

        private void ProtocolSocket_DOSDetected(ProtocolSocket sender) {
            DOSDetected?.Invoke(sender);
        }

        private void ProtocolSocket_ReceiveProgressChanged(ProtocolSocket sender, int bytesReceived, int bytesToReceive) {
            ReceiveProgressChanged?.Invoke(sender, bytesReceived, bytesToReceive);
        }

        private void ProtocolSocket_PacketReceived(ProtocolSocket sender, PacketReceivedEventArgs PacketReceivedEventArgs) {
            PacketReceived?.Invoke(sender, PacketReceivedEventArgs);
        }

        /// <summary>
        /// Ignores any connection made from specified host
        /// </summary>
        /// <param name="host">Host ip to filter</param>
        public void AddHostFilter(string host) {
            if (!blockedHosts.Contains(host))
                blockedHosts.Add(host);
        }

        public void Dispose() {
            Stop();
            ListeningStateChanged = null;
        }
    }
}
