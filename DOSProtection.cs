namespace ProtocolSocket
{
    public class DOSProtection
    {
        public int MaxPackets { get; set; }
        public int OverTime { get; set; }

        public DOSProtection(int maxPackets, int overTime)
        {
            MaxPackets = maxPackets;
            OverTime = overTime;
        }
    }
}
