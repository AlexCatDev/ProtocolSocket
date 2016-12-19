using System;
using System.Collections.Generic;
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
        public delegate void SendProgressChangedEventHandler(ProtocolSocket sender, int send);

        //Client events
        public event ReceiveProgressChangedEventHandler ReceiveProgressChanged;
        public event PacketReceivedEventHandler PacketReceived;
        public event ClientStateChangedEventHandler ClientStateChanged;
        public event DOSDetectedEventHandler DOSDetected;
        public event SendProgressChangedEventHandler SendProgressChanged;

        public bool Listening { get; private set; }
        public ProtocolServerOptions ServerSocketOptions { get; private set; }

        public List<ProtocolSocket> ConnectedClients {
            get {
                lock (syncLock) { return connectedClients; }
            }
        }

        Socket listeningSocket;
        List<ProtocolSocket> connectedClients;
        object syncLock;

        public ProtocolServer(ProtocolServerOptions serverSocketOptions)
        {
            Listening = false;
            syncLock = new object();
            ServerSocketOptions = serverSocketOptions;
            connectedClients = new List<ProtocolSocket>();

        }

        public void Listen()
        {
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

        public void Stop()
        {
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

        private void AcceptCallBack(IAsyncResult iar)
        {
            try {
                Socket accepted = listeningSocket.EndAccept(iar);
                accepted.NoDelay = true;
                ProtocolSocket protocolSocket = new ProtocolSocket(accepted, ServerSocketOptions.ProtocolSocketOptions);

                protocolSocket.ClientStateChanged += ProtocolSocket_ClientStateChanged;
                protocolSocket.PacketReceived += ProtocolSocket_PacketReceived;
                protocolSocket.ReceiveProgressChanged += ProtocolSocket_ReceiveProgressChanged;
                protocolSocket.SendProgressChanged += ProtocolSocket_SendProgressChanged;
                protocolSocket.DOSDetected += ProtocolSocket_DOSDetected;

                protocolSocket.Start();
                listeningSocket.BeginAccept(AcceptCallBack, null);
            } catch {
                //MessageBox.Show(ex.Message + " \n\n [" + ex.StackTrace + "]");
            }
        }

        public void BroadcastPacket(byte[] packet, ProtocolSocket exception = null)
        {
            lock (syncLock) {
                for (int i = 0; i > connectedClients.Count; i++) {
                    var client = connectedClients[i];
                    if (client != exception) {
                        try { client.SendPacket(packet); } catch { }
                    }
                }
            }
        }

        private void ProtocolSocket_ClientStateChanged(ProtocolSocket sender, Exception Message, bool Connected)
        {
            lock (syncLock) {
                if (Connected) {
                    connectedClients.Add(sender);
                } else {
                    connectedClients.Remove(sender);
                }
                ClientStateChanged?.Invoke(sender, Message, Connected);
            }
        }

        private void ProtocolSocket_SendProgressChanged(ProtocolSocket sender, int Send)
        {
            SendProgressChanged?.Invoke(sender, Send);
        }

        private void ProtocolSocket_DOSDetected(ProtocolSocket sender)
        {
            DOSDetected?.Invoke(sender);
        }

        private void ProtocolSocket_ReceiveProgressChanged(ProtocolSocket sender, int Received, int BytesToReceive)
        {
            ReceiveProgressChanged?.Invoke(sender, Received, BytesToReceive);
        }

        private void ProtocolSocket_PacketReceived(ProtocolSocket sender, PacketReceivedEventArgs PacketReceivedEventArgs)
        {
            PacketReceived?.Invoke(sender, PacketReceivedEventArgs);
        }

        public void Dispose()
        {
            Stop();
            ListeningStateChanged = null;
        }
    }
}
