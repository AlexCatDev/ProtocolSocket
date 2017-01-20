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
        public delegate void PacketReceivedEventHandler(ProtocolClient sender, PacketReceivedEventArgs packetReceivedEventArgs);
        public delegate void DOSDetectedEventHandler(ProtocolClient sender);
        public delegate void ClientStateChangedEventHandler(ProtocolClient sender, Exception message, bool connected);
        public delegate void ReceiveProgressChangedEventHandler(ProtocolClient sender, int received, int bytesToReceive);
        public delegate void PacketSendEventHandler(ProtocolClient sender, int bytesSend);
        public delegate void ConnectionEstablishedEventHandler(ProtocolClient sender);
        public delegate void ConnectionErrorEventHandler(ProtocolClient sender, Exception exception);

        //Client events
        public event ConnectionEstablishedEventHandler ConnectionEstablished;
        public event ConnectionErrorEventHandler ConnectionError;
        public event ReceiveProgressChangedEventHandler ReceiveProgressChanged;
        public event PacketReceivedEventHandler PacketReceived;
        public event DOSDetectedEventHandler DOSDetected;
        public event PacketSendEventHandler PacketSend;

        public bool Listening { get; private set; }
        public ProtocolServerOptions ServerOptions { get; private set; }

        public IList<ProtocolClient> ConnectedClients {
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
        List<ProtocolClient> connectedClients;
        List<string> blockedHosts;
        object syncLock;

        public ProtocolServer(ProtocolServerOptions serverOptions) {
            Listening = false;
            syncLock = new object();
            ServerOptions = serverOptions;
            connectedClients = new List<ProtocolClient>();
            blockedHosts = new List<string>();
        }

        public void Listen() {
            if (Listening) {
                throw new InvalidOperationException("Server is already listening. Please call Close() before calling Listen()");
            } else {
                if (ServerOptions.ListenEndPoint == null)
                    throw new InvalidOperationException("Please specify a endpoint to listen on before calling Listen()");

                listeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                listeningSocket.Bind(ServerOptions.ListenEndPoint);
                listeningSocket.Listen(ServerOptions.MaxConnectionQueue);
                Listening = true;
                ListeningStateChanged?.Invoke(this, Listening);
                listeningSocket.BeginAccept(AcceptCallBack, null);
            }
        }

        public void Stop() {
            if (Listening) {
                listeningSocket.Close();
                foreach (var client in connectedClients) {
                    client.Close();
                }
                connectedClients.Clear();
                BlockedHosts.Clear();
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
                ProtocolClient protocolClient = new ProtocolClient(accepted, ServerOptions.ClientOptions);

                protocolClient.ConnectionError += ProtocolClient_ConnectionError;
                protocolClient.ConnectionEstablished += ProtocolClient_ConnectionEstablished;
                protocolClient.PacketReceived += ProtocolClient_PacketReceived;
                protocolClient.ReceiveProgressChanged += ProtocolClient_ReceiveProgressChanged;
                protocolClient.PacketSend += ProtocolClient_PacketSend;
                protocolClient.DOSDetected += ProtocolClient_DOSDetected;

                protocolClient.Start();
                listeningSocket.BeginAccept(AcceptCallBack, null);
            } catch {
                //MessageBox.Show(ex.Message + " \n\n [" + ex.StackTrace + "]");
            }
        }

        private void ProtocolClient_ConnectionEstablished(ProtocolClient sender) {
            lock (syncLock) {
                connectedClients.Add(sender);
                ConnectionEstablished?.Invoke(sender);
            }
        }

        private void ProtocolClient_ConnectionError(ProtocolClient sender, Exception exception) {
            lock(syncLock) {
                connectedClients.Remove(sender);
                ConnectionError?.Invoke(sender, exception);
            }
        }

        public void Broadcast(byte[] packet, ProtocolClient exception = null) {
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

        private void ProtocolClient_PacketSend(ProtocolClient sender, int Send) {
            PacketSend?.Invoke(sender, Send);
        }

        private void ProtocolClient_DOSDetected(ProtocolClient sender) {
            DOSDetected?.Invoke(sender);
        }

        private void ProtocolClient_ReceiveProgressChanged(ProtocolClient sender, int bytesReceived, int bytesToReceive) {
            ReceiveProgressChanged?.Invoke(sender, bytesReceived, bytesToReceive);
        }

        private void ProtocolClient_PacketReceived(ProtocolClient sender, PacketReceivedEventArgs PacketReceivedEventArgs) {
            PacketReceived?.Invoke(sender, PacketReceivedEventArgs);
        }

        /// <summary>
        /// Ignores any connection made from specified ip
        /// </summary>
        /// <param name="ip">Remote ip of host to filter</param>
        public void AddHostFilter(string ip) {
            if (!blockedHosts.Contains(ip))
                blockedHosts.Add(ip);
        }

        public void Dispose() {
            Stop();
            ListeningStateChanged = null;
        }
    }
}
