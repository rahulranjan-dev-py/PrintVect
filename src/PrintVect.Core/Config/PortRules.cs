namespace PrintVect.Core.Config
{
    /// <summary>Which TCP/UDP ports PrintVect may be configured to use (brief, section 6).</summary>
    public static class PortRules
    {
        /// <summary>Reserved for the Phase 2 raw relay; never used by version 1.</summary>
        public const int RawRelayPort = 9100;

        /// <summary>IPP port; never used.</summary>
        public const int IppPort = 631;

        public const int MinPort = 1024;
        public const int MaxPort = 65535;

        public static bool IsAllowed(int port)
        {
            return port >= MinPort && port <= MaxPort && port != RawRelayPort && port != IppPort;
        }
    }
}
