using System;

namespace ProtocolSocket
{
    public class ProtocolSocketOptions
    {

        public DOSProtection DOSProtection { get; set; }
        /// <summary>
        /// The amount of bytes to buffer before being written to the stream
        /// </summary>
        public int BufferSize { get; private set; }
        /// <summary>
        /// The amount of bytes max allowed to be received
        /// </summary>
        public int MaxPacketSize { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="receiveBufferSize">The amount of bytes to buffer before being written to the stream</param>
        public ProtocolSocketOptions(int receiveBufferSize = 1024*8)
        {
            MaxPacketSize = ushort.MaxValue - 4;
            if (receiveBufferSize > 4)
                BufferSize = receiveBufferSize;
            else
                BufferSize = 1024 * 8;
        }

        /// <summary>
        /// Setups DOS protection
        /// </summary>
        /// <param name="DOSProtection"></param>
        public void SetDOSProtection(DOSProtection DOSProtection) {
            this.DOSProtection = DOSProtection;
        }
    }
}
