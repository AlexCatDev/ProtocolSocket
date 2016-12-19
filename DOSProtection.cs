namespace ProtocolSocket
{
    public class DOSProtection
    {
        /// <summary>
        /// Max packets allowed over time
        /// </summary>
        public int MaxPackets { get; set; }
        /// <summary>
        /// The time in milliseconds
        /// </summary>
        public int Delta { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="maxPackets">Max packets over time</param>
        /// <param name="delta">Time for max packets in milliseconds</param>
        public DOSProtection(int maxPackets, int delta)
        {
            MaxPackets = maxPackets;
            Delta = delta;
        }
    }
}
