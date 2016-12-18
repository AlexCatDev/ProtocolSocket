namespace ProtocolSocket
{
    public class ProtocolServerOptions
    {
        public ProtocolSocketOptions ProtocolSocketOptions { get; set; }
        public int MaxConnectionQueue { get; set; }

        public ProtocolServerOptions(ProtocolSocketOptions protocolSocketOptions, int maxConnectionQueue = 20)
        {
            ProtocolSocketOptions = protocolSocketOptions;
            MaxConnectionQueue = maxConnectionQueue;
        }
    }
}
