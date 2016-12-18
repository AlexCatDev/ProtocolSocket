using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace ProtocolSocket
{
    public class ProtocolServer : IDisposable
    {
        //Listener event delegates
        public delegate void ListeningStateChangedEventHandler(ProtocolServer sender, bool Listening);

        //Listener events
        public event ListeningStateChangedEventHandler ListeningStateChanged;

        //Client event delegates
        public delegate void PacketReceivedEventHandler(ProtocolSocket sender, PacketReceivedEventArgs PacketReceivedEventArgs);
        public delegate void DOSDetectedEventHandler(ProtocolSocket sender);
        public delegate void ClientStateChangedEventHandler(ProtocolSocket sender, Exception Message, bool Connected);
        public delegate void ReceiveProgressChangedEventHandler(ProtocolSocket sender, int Received, int BytesToReceive);
        public delegate void SendProgressChangedEventHandler(ProtocolSocket sender, int Send);

        //Client events
        public event ReceiveProgressChangedEventHandler ReceiveProgressChanged;
        public event PacketReceivedEventHandler PacketReceived;
        public event ClientStateChangedEventHandler ClientStateChanged;
        public event DOSDetectedEventHandler DOSDetected;
        public event SendProgressChangedEventHandler SendProgressChanged;

        public List<ProtocolSocket> ConnectedClients {
            get {
                lock (syncLock) { return connectedClients; }
            }
        }

        public bool Running { get; private set; }
        public int Port { get; private set; }
        public ProtocolServerOptions ServerSocketOptions { get; private set; }

        Socket listener;
        List<ProtocolSocket> connectedClients;
        object syncLock;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="Port">Port to listen on</param>
        /// <param name="MaxPacketSize">Determines the max allowed packet size to be received, unless specifically set by the client object</param>
        public ProtocolServer(int port, ProtocolServerOptions serverSocketOptions)
        {
            Port = port;
            Running = false;
            syncLock = new object();
            ServerSocketOptions = serverSocketOptions;
            connectedClients = new List<ProtocolSocket>();

        }

        public void Start()
        {
            if (Running) {
                throw new InvalidOperationException("Listener is already running.");
            } else {
                listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                listener.Bind(new IPEndPoint(0, Port));
                listener.Listen(ServerSocketOptions.MaxConnectionQueue);
                Running = true;
                ListeningStateChanged?.Invoke(this, Running);
                listener.BeginAccept(AcceptCallBack, null);
            }
        }

        public void Stop()
        {
            if (Running) {
                listener.Close();
                listener = null;
                foreach (var client in connectedClients) {
                    client.Dispose();
                }
                connectedClients.Clear();
                Running = false;
                ListeningStateChanged?.Invoke(this, Running);
            } else {
                throw new InvalidOperationException("Listener isn't running.");
            }
        }

        private void AcceptCallBack(IAsyncResult iar)
        {
            try {
                Socket accepted = listener.EndAccept(iar);
                ProtocolSocket protocolSocket = new ProtocolSocket(accepted, ServerSocketOptions.ProtocolSocketOptions);

                protocolSocket.ClientStateChanged += ProtocolSocket_ClientStateChanged;
                protocolSocket.PacketReceived += ProtocolSocket_PacketReceived;
                protocolSocket.ReceiveProgressChanged += ProtocolSocket_ReceiveProgressChanged;
                protocolSocket.SendProgressChanged += ProtocolSocket_SendProgressChanged;
                protocolSocket.DOSDetected += ProtocolSocket_DOSDetected;

                protocolSocket.Start();
                listener.BeginAccept(AcceptCallBack, null);
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
