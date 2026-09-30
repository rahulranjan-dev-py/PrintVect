using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace PrintVect.Core.Protocol
{
    /// <summary>The other side sent something that is not PrintVect protocol version 1.</summary>
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
        public ProtocolException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Wire format of one TCP request (brief, section 6): "PVCT", 4-byte little-endian header
    /// length, UTF-8 JSON header, then the raw file bytes. The host answers one JSON line and
    /// closes. Everything here is pure stream code so it is unit-tested without a network.
    /// </summary>
    public static class Framing
    {
        private static readonly UTF8Encoding Utf8Strict = new UTF8Encoding(false, true);
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None
        };

        public static byte[] EncodeHeader(RequestHeader header)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));

            byte[] json = Utf8Strict.GetBytes(JsonConvert.SerializeObject(header, JsonSettings));
            if (json.Length > ProtocolConstants.MaxHeaderBytes)
            {
                throw new ProtocolException("The request header is too large (" + json.Length + " bytes).");
            }

            var frame = new byte[8 + json.Length];
            Buffer.BlockCopy(ProtocolConstants.Magic, 0, frame, 0, 4);
            WriteInt32LittleEndian(frame, 4, json.Length);
            Buffer.BlockCopy(json, 0, frame, 8, json.Length);
            return frame;
        }

        public static RequestHeader DecodeHeaderJson(byte[] json)
        {
            string text;
            try
            {
                text = Utf8Strict.GetString(json);
            }
            catch (DecoderFallbackException ex)
            {
                throw new ProtocolException("The request header is not valid UTF-8 text.", ex);
            }

            RequestHeader header;
            try
            {
                header = JsonConvert.DeserializeObject<RequestHeader>(text);
            }
            catch (JsonException ex)
            {
                throw new ProtocolException("The request header is not valid JSON: " + ex.Message, ex);
            }

            if (header == null || string.IsNullOrEmpty(header.Type))
            {
                throw new ProtocolException("The request header has no \"type\".");
            }
            return header;
        }

        public static Task WriteHeaderAsync(Stream stream, RequestHeader header, CancellationToken ct)
        {
            byte[] frame = EncodeHeader(header);
            return stream.WriteAsync(frame, 0, frame.Length, ct);
        }

        public static async Task<RequestHeader> ReadHeaderAsync(Stream stream, CancellationToken ct)
        {
            var prefix = new byte[8];
            await ReadExactlyAsync(stream, prefix, 8, ct).ConfigureAwait(false);

            for (int i = 0; i < 4; i++)
            {
                if (prefix[i] != ProtocolConstants.Magic[i])
                {
                    throw new ProtocolException("The connection did not start with \"" + ProtocolConstants.MagicText
                                                + "\"; the other side is not PrintVect, or is a different version.");
                }
            }

            int length = ReadInt32LittleEndian(prefix, 4);
            if (length <= 0 || length > ProtocolConstants.MaxHeaderBytes)
            {
                throw new ProtocolException("Invalid request header length " + length + ".");
            }

            var json = new byte[length];
            await ReadExactlyAsync(stream, json, length, ct).ConfigureAwait(false);
            return DecodeHeaderJson(json);
        }

        public static string EncodeReply(object reply)
        {
            return JsonConvert.SerializeObject(reply, JsonSettings);
        }

        public static async Task WriteReplyAsync(Stream stream, object reply, CancellationToken ct)
        {
            byte[] bytes = Utf8Strict.GetBytes(EncodeReply(reply) + "\n");
            await stream.WriteAsync(bytes, 0, bytes.Length, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }

        /// <summary>Reads the host's single reply line. The host closes after it, so reading to a newline or EOF is enough.</summary>
        public static async Task<string> ReadReplyLineAsync(Stream stream, CancellationToken ct)
        {
            var collected = new MemoryStream();
            var buffer = new byte[4096];
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                collected.Write(buffer, 0, read);
                if (collected.Length > ProtocolConstants.MaxReplyBytes)
                {
                    throw new ProtocolException("The reply is too large.");
                }
                if (Array.IndexOf(buffer, (byte)'\n', 0, read) >= 0)
                {
                    break;
                }
            }

            if (collected.Length == 0)
            {
                throw new EndOfStreamException("The other side closed the connection without a reply.");
            }

            string text = Encoding.UTF8.GetString(collected.ToArray());
            int newline = text.IndexOf('\n');
            return (newline >= 0 ? text.Substring(0, newline) : text).TrimEnd('\r');
        }

        public static T ParseReply<T>(string line) where T : class
        {
            try
            {
                T reply = JsonConvert.DeserializeObject<T>(line);
                if (reply == null)
                {
                    throw new ProtocolException("The reply was empty.");
                }
                return reply;
            }
            catch (JsonException ex)
            {
                string shown = line.Length > 200 ? line.Substring(0, 200) + "..." : line;
                throw new ProtocolException("The reply was not valid JSON: " + shown, ex);
            }
        }

        public static async Task ReadExactlyAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
        {
            int offset = 0;
            while (offset < count)
            {
                int read = await stream.ReadAsync(buffer, offset, count - offset, ct).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new EndOfStreamException("The connection ended after " + offset + " of " + count + " bytes.");
                }
                offset += read;
            }
        }

        /// <summary>Copies exactly <paramref name="count"/> bytes; a shorter source is an error, never silently accepted.</summary>
        public static async Task<long> CopyExactlyAsync(Stream source, Stream destination, long count, IProgress<long> progress, CancellationToken ct)
        {
            var buffer = new byte[ProtocolConstants.CopyBufferSize];
            long copied = 0;
            while (copied < count)
            {
                int wanted = (int)Math.Min(buffer.Length, count - copied);
                int read = await source.ReadAsync(buffer, 0, wanted, ct).ConfigureAwait(false);
                if (read <= 0)
                {
                    throw new EndOfStreamException("The connection ended after " + copied + " of " + count + " bytes.");
                }
                await destination.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
                copied += read;
                if (progress != null)
                {
                    progress.Report(copied);
                }
            }
            return copied;
        }

        /// <summary>Reads and discards up to <paramref name="count"/> bytes so a refusal can still be delivered to the sender.</summary>
        public static async Task<long> DrainAsync(Stream source, long count, CancellationToken ct)
        {
            var buffer = new byte[ProtocolConstants.CopyBufferSize];
            long drained = 0;
            while (drained < count)
            {
                int wanted = (int)Math.Min(buffer.Length, count - drained);
                int read = await source.ReadAsync(buffer, 0, wanted, ct).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }
                drained += read;
            }
            return drained;
        }

        public static void WriteInt32LittleEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        public static int ReadInt32LittleEndian(byte[] buffer, int offset)
        {
            return buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24);
        }
    }
}
