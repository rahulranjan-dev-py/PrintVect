using System;
using System.Text;

namespace PrintVect.Core.Protocol
{
    /// <summary>Fixed numbers of protocol version 1 (brief, section 6; docs/protocol.md).</summary>
    public static class ProtocolConstants
    {
        public const int Version = 1;

        /// <summary>First four bytes of every TCP request.</summary>
        public const string MagicText = "PVCT";
        public static readonly byte[] Magic = Encoding.ASCII.GetBytes(MagicText);

        /// <summary>Text a client broadcasts over UDP to find hosts (milestone M3).</summary>
        public const string DiscoveryRequest = "PVECT-DISCOVER 1";

        public const int MaxHeaderBytes = 64 * 1024;
        public const long MaxFileBytes = 200L * 1024 * 1024;
        public const int MaxReplyBytes = 256 * 1024;
        public const int CopyBufferSize = 64 * 1024;

        public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan TransferTimeout = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(2);

        /// <summary>How long a host keeps the connection open waiting for "printed" before answering with the current state.</summary>
        public static readonly TimeSpan ReplyWait = TimeSpan.FromSeconds(60);
    }
}
