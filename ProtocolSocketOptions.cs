using System;

namespace ProtocolSocket
{
    public class ProtocolSocketOptions
    {
        public DOSProtection DOSProtection { get; set; }
        public int MaxPacketSize { get; set; }
        public bool AllowZeroLengthPackets { get; set; }

        public ProtocolSocketOptions()
        {
            MaxPacketSize = Int32.MaxValue;
            AllowZeroLengthPackets = false;
        }
    }
}
