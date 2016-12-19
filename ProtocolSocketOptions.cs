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
            MaxPacketSize = ushort.MaxValue - 4;
            AllowZeroLengthPackets = false;
        }
    }
}
