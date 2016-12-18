using System;

namespace ProtocolSocket
{
    public class PacketReceivedEventArgs : EventArgs
    {
        public byte[] Packet { get; }
        public int Length { get; }

        public PacketReceivedEventArgs(byte[] packet)
        {
            Packet = packet;
            Length = packet.Length;
        }
    }
}
