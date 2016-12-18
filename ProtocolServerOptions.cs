using System.Net;

namespace ProtocolSocket
{
    public class ProtocolServerOptions
    {
        public ProtocolSocketOptions ProtocolSocketOptions { get; set; }
        public int MaxConnectionQueue { get; set; }
        public EndPoint ListenEndPoint { get; set; }

        public ProtocolServerOptions(ProtocolSocketOptions protocolSocketOptions, EndPoint listenEndPoint, int maxConnectionQueue = 20)
        {
            ProtocolSocketOptions = protocolSocketOptions;
            MaxConnectionQueue = maxConnectionQueue;
            ListenEndPoint = listenEndPoint;
        }
    }
}
