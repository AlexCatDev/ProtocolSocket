# ProtocolSocket
A very simple lightweight and fast tcp socket library that handles message framing and has optional dos protection.

Simple example on how to use:

```csharp
            //Declare socket options to use for both server and client
            ProtocolSocketOptions socketOptions = new ProtocolSocketOptions();
            //Set dos protection to allow up to 10 packets every 100 milliseconds
            socketOptions.DOSProtection = new DOSProtection(10,100);

            //Declare server options
            ProtocolServerOptions serverOptions = new ProtocolServerOptions(socketOptions);
            ProtocolServer server = new ProtocolServer(9090, serverOptions);

            //if dos was detected fire this event
            server.DOSDetected += (s) => {
                Console.Title = "A dos was detected " + s.RemoteEndPoint + " warning.";
                //Could call s.Close();
            };

            //S = sender, e = exception (can be null), connected = if client is connected
            server.ClientStateChanged += (s, e, connected) => {
                Console.WriteLine("Client " + s.RemoteEndPoint + " changed state connected? " + 
                connected + " message " + e?.Message);
            };
            server.PacketReceived += (s, e) => {
                Console.WriteLine("Received " + e.Length + " from " + s.RemoteEndPoint);
            };

            //Start server
            server.Start();

            ProtocolSocket.ProtocolSocket sock = new ProtocolSocket.ProtocolSocket(socketOptions);
            sock.Connect("127.0.0.1", 9090);
            while (true) {
                sock.SendPacket(new byte[1024]);
                Thread.Sleep(5);   
            }
```
