using System.Net;

namespace ProtocolSocket
{
    public class ProtocolServerOptions
    {
        public ProtocolClientOptions ClientOptions { get; set; }
        public int MaxConnectionQueue { get; set; }
        public EndPoint ListenEndPoint { get; set; }

        public ProtocolServerOptions(ProtocolClientOptions clientOptions, EndPoint listenEndPoint, int maxConnectionQueue = 20)
        {
            ClientOptions = clientOptions;
            MaxConnectionQueue = maxConnectionQueue;
            ListenEndPoint = listenEndPoint;
        }
    }
}
