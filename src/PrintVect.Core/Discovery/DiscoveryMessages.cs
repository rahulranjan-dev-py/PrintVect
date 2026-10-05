using System;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Discovery
{
    /// <summary>The two discovery datagrams (docs/protocol.md): the ASCII request and the JSON reply.</summary>
    public static class DiscoveryMessages
    {
        private const string RequestPrefix = "PVECT-DISCOVER ";
        public static readonly byte[] RequestBytes = Encoding.ASCII.GetBytes(ProtocolConstants.DiscoveryRequest);
        public const int MaxReplyBytes = 64 * 1024;

        /// <summary>True for "PVECT-DISCOVER n" (any version; the reply carries ours).</summary>
        public static bool IsRequest(byte[] data, out int version)
        {
            version = 0;
            if (data == null || data.Length < RequestPrefix.Length + 1 || data.Length > 64) return false;
            string text;
            try
            {
                text = Encoding.ASCII.GetString(data).Trim('\0', ' ', '\r', '\n', '\t');
            }
            catch (Exception)
            {
                return false;
            }
            if (!text.StartsWith(RequestPrefix, StringComparison.Ordinal)) return false;
            return int.TryParse(text.Substring(RequestPrefix.Length).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out version);
        }

        public static byte[] EncodeReply(ListReply reply)
        {
            if (reply == null) throw new ArgumentNullException(nameof(reply));
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(reply));
        }

        /// <summary>The reply, or null when the datagram is not a PrintVect reply.</summary>
        public static ListReply ParseReply(byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > MaxReplyBytes) return null;
            try
            {
                string json = Encoding.UTF8.GetString(data).TrimEnd('\0');
                if (json.Length == 0 || json[0] != '{') return null;
                ListReply reply = JsonConvert.DeserializeObject<ListReply>(json);
                if (reply == null || !string.Equals(reply.App, AppInfo.ProductName, StringComparison.Ordinal)) return null;
                if (reply.Printers == null) reply.Printers = new System.Collections.Generic.List<PrinterInfo>();
                return reply;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
