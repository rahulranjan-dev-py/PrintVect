using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Protocol
{
    /// <summary>The host PC could not be reached at all. The message says what to do next.</summary>
    public sealed class HostUnreachableException : Exception
    {
        public HostUnreachableException(string message) : base(message) { }
        public HostUnreachableException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// The client side of the TCP protocol: send a job, ask for a job's status, or ask a host
    /// for its shared printers. Used by pvct-send now and by the client role in M2/M3.
    /// </summary>
    public sealed class JobClient
    {
        public JobClient(string host, int port)
        {
            if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("A host name or IP address is required.", nameof(host));
            Host = host.Trim();
            Port = port;
        }

        public string Host { get; }
        public int Port { get; }
        public TimeSpan ConnectTimeout { get; set; } = ProtocolConstants.ConnectTimeout;
        public TimeSpan TransferTimeout { get; set; } = ProtocolConstants.TransferTimeout;

        public async Task<ListReply> ListPrintersAsync(CancellationToken ct)
        {
            string line = await ExchangeAsync(RequestHeader.ForList(), null, null, ct).ConfigureAwait(false);
            return Framing.ParseReply<ListReply>(line);
        }

        public async Task<JobReply> QueryStatusAsync(string jobId, CancellationToken ct)
        {
            string line = await ExchangeAsync(RequestHeader.ForStatus(jobId), null, null, ct).ConfigureAwait(false);
            return Framing.ParseReply<JobReply>(line);
        }

        /// <summary>Sends the file described by <paramref name="header"/>; the header's Size is set from the file.</summary>
        public async Task<JobReply> SendJobAsync(RequestHeader header, string filePath, IProgress<long> progress, CancellationToken ct)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (header.Type != RequestTypes.Job) throw new ArgumentException("The header must be a job header.", nameof(header));

            var file = new FileInfo(filePath);
            if (!file.Exists)
            {
                throw new FileNotFoundException("The file to print does not exist: " + filePath, filePath);
            }
            if (file.Length <= 0)
            {
                throw new InvalidDataException("The file to print is empty: " + filePath);
            }
            if (file.Length > ProtocolConstants.MaxFileBytes)
            {
                throw new InvalidDataException("The file is larger than 200 MB, which PrintVect does not send: " + filePath);
            }
            header.Size = file.Length;
            if (string.IsNullOrEmpty(header.FileName))
            {
                header.FileName = file.Name;
            }

            Log.Info(header.JobId, string.Format("Sending \"{0}\" ({1:N0} bytes, {2}) to {3}:{4} for printer \"{5}\".",
                header.FileName, header.Size, header.Format, Host, Port, header.PrinterId));
            string line = await ExchangeAsync(header, filePath, progress, ct).ConfigureAwait(false);
            JobReply reply = Framing.ParseReply<JobReply>(line);
            Log.Info(header.JobId, "Host answered: ok=" + reply.Ok + ", state=" + reply.State + ", " + reply.Message);
            return reply;
        }

        private async Task<string> ExchangeAsync(RequestHeader header, string bodyPath, IProgress<long> progress, CancellationToken ct)
        {
            using (var client = new TcpClient())
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            using (timeout.Token.Register(() => SafeClose(client)))
            {
                client.NoDelay = true;
                await ConnectAsync(client, timeout.Token).ConfigureAwait(false);
                timeout.CancelAfter(TransferTimeout);

                try
                {
                    using (NetworkStream stream = client.GetStream())
                    {
                        await Framing.WriteHeaderAsync(stream, header, timeout.Token).ConfigureAwait(false);
                        if (bodyPath != null)
                        {
                            using (var file = new FileStream(bodyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                       ProtocolConstants.CopyBufferSize, true))
                            {
                                await Framing.CopyExactlyAsync(file, stream, header.Size, progress, timeout.Token).ConfigureAwait(false);
                            }
                        }
                        await stream.FlushAsync(timeout.Token).ConfigureAwait(false);

                        // Tell the host the body is complete; a short body is then detected immediately.
                        try { client.Client.Shutdown(SocketShutdown.Send); }
                        catch (SocketException ex) { Log.Warn(header.JobId, "Send-side shutdown failed (continuing): " + ex.Message); }

                        return await Framing.ReadReplyLineAsync(stream, timeout.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    throw new TimeoutException(Host + " did not finish within " + TransferTimeout.TotalMinutes
                                               + " minutes. Check the network connection and try again.", ex);
                }
                catch (IOException ex)
                {
                    throw new HostUnreachableException(Host + " stopped answering during the transfer (" + ex.Message
                                                       + "). Check that PrintVect on that PC is still running with sharing ON.", ex);
                }
                catch (ObjectDisposedException ex)
                {
                    throw new HostUnreachableException("The connection to " + Host + " was closed.", ex);
                }
            }
        }

        private async Task ConnectAsync(TcpClient client, CancellationToken ct)
        {
            Task connect;
            try
            {
                connect = client.ConnectAsync(Host, Port);
            }
            catch (SocketException ex)
            {
                throw new HostUnreachableException(DescribeConnectError(ex), ex);
            }

            Task finished = await Task.WhenAny(connect, Task.Delay(ConnectTimeout, ct)).ConfigureAwait(false);
            if (finished != connect)
            {
                ct.ThrowIfCancellationRequested();
                SafeClose(client);
                throw new HostUnreachableException(Host + " did not answer within " + ConnectTimeout.TotalSeconds
                                                   + " seconds. Check that the PC is switched on, connected to the office network, "
                                                   + "and running PrintVect with sharing ON.");
            }

            try
            {
                await connect.ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                throw new HostUnreachableException(DescribeConnectError(ex), ex);
            }
        }

        private string DescribeConnectError(SocketException ex)
        {
            switch (ex.SocketErrorCode)
            {
                case SocketError.ConnectionRefused:
                    return Host + " is reachable but nothing is listening on port " + Port
                           + ". On that PC, open PrintVect and turn sharing ON, and check that the Windows Firewall allows it.";
                case SocketError.HostNotFound:
                case SocketError.NoData:
                case SocketError.TryAgain:
                    return "The name \"" + Host + "\" could not be found on the network. Use the PC's IP address instead.";
                case SocketError.TimedOut:
                case SocketError.HostUnreachable:
                case SocketError.NetworkUnreachable:
                    return Host + " is not reachable on the network. Check that the PC is switched on and its network cable is plugged in.";
                default:
                    return "Could not connect to " + Host + ":" + Port + " (" + ex.SocketErrorCode + ": " + ex.Message + ").";
            }
        }

        private static void SafeClose(TcpClient client)
        {
            try { client.Close(); }
            catch (Exception ex) { Log.Warn("Closing the connection failed: " + ex.Message); }
        }
    }
}
