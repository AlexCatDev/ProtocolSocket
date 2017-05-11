using System;

namespace ProtocolSocket
{
    public class ProtocolClientOptions
    {

        public DOSProtection DOSProtection { get; set; }
        /// <summary>
        /// The amount of bytes to buffer before being written to the stream
        /// </summary>
        public int BufferSize { get; private set; }
        /// <summary>
        /// The amount of bytes maximum allowed to be received
        /// </summary>
        public int MaxPacketSize { get; set; }
        /// <summary>
        /// The amount of bytes minimum allowed to be received
        /// </summary>
        public int MinPacketSize { get; set; }

        public int PingInterval { get; set; }
        public int MaxPingAttempts { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="receiveBufferSize">The amount of bytes to buffer before being written to the stream</param>
        public ProtocolClientOptions(int receiveBufferSize = 114688)
        {
            MaxPacketSize = receiveBufferSize;
            MinPacketSize = 1;

            PingInterval = NetConstants.PingInterval;
            MaxPingAttempts = NetConstants.MaxPingAttempts;

            if (receiveBufferSize > 0)
                BufferSize = receiveBufferSize;
            else
                throw new Exception("Buffer size is too small.");
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
