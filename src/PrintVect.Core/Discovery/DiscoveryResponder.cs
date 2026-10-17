using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Discovery
{
    /// <summary>
    /// Host side of discovery: listens on UDP port 9150 on every address and answers each
    /// "PVECT-DISCOVER 1" with the printer list, by unicast, from the address that is on the
    /// asker's subnet. Runs while sharing is ON.
    /// </summary>
    public sealed class DiscoveryResponder : IDisposable
    {
        private readonly Func<IPAddress, ListReply> _replyBuilder;
        private readonly object _gate = new object();
        private UdpClient _socket;
        private CancellationTokenSource _stopping;
        private Task _loop;
        private int _answered;

        /// <param name="replyBuilder">Builds the reply for the asker's address (picks the right local IP).</param>
        public DiscoveryResponder(Func<IPAddress, ListReply> replyBuilder)
        {
            if (replyBuilder == null) throw new ArgumentNullException(nameof(replyBuilder));
            _replyBuilder = replyBuilder;
        }

        public int Port { get; private set; }

        public bool IsListening
        {
            get { lock (_gate) { return _socket != null; } }
        }

        public int RequestsAnswered
        {
            get { return Volatile.Read(ref _answered); }
        }

        /// <summary>Binds UDP port <paramref name="port"/> (0 = any free port, for tests). Throws SocketException when it is taken.</summary>
        public void Start(int port)
        {
            lock (_gate)
            {
                if (_socket != null) return;
                var socket = new UdpClient();
                try
                {
                    socket.ExclusiveAddressUse = false;
                    socket.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    socket.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                    UdpSockets.IgnoreConnectionResets(socket);
                }
                catch (Exception)
                {
                    socket.Close();
                    throw;
                }
                _socket = socket;
                Port = ((IPEndPoint)socket.Client.LocalEndPoint).Port;
                _stopping = new CancellationTokenSource();
                _loop = ReceiveLoopAsync(socket, _stopping.Token);
            }
            Log.Info("Discovery: answering \"" + ProtocolConstants.DiscoveryRequest + "\" on UDP port " + Port + " on every network card.");
        }

        public void Stop()
        {
            UdpClient socket;
            CancellationTokenSource stopping;
            lock (_gate)
            {
                socket = _socket;
                stopping = _stopping;
                _socket = null;
                _stopping = null;
            }
            if (socket == null) return;
            stopping.Cancel();
            try { socket.Close(); }
            catch (Exception ex) { Log.Warn("Discovery socket did not close cleanly: " + ex.Message); }
            stopping.Dispose();
            Log.Info("Discovery: stopped answering on UDP port " + Port + ".");
        }

        private async Task ReceiveLoopAsync(UdpClient socket, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                UdpReceiveResult received;
                try
                {
                    received = await socket.ReceiveAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException ex)
                {
                    if (ct.IsCancellationRequested) return;
                    Log.Warn("Discovery receive failed (" + ex.SocketErrorCode + "); still listening.");
                    continue;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) return;
                    Log.Error("Discovery receive failed unexpectedly; still listening.", ex);
                    continue;
                }

                int version;
                if (!DiscoveryMessages.IsRequest(received.Buffer, out version))
                {
                    continue;   // somebody else's datagram
                }
                Answer(socket, received.RemoteEndPoint, version);
            }
        }

        private void Answer(UdpClient socket, IPEndPoint asker, int version)
        {
            try
            {
                ListReply reply = _replyBuilder(asker.Address);
                byte[] bytes = DiscoveryMessages.EncodeReply(reply);
                socket.Send(bytes, bytes.Length, asker);
                Interlocked.Increment(ref _answered);
                Log.Info("Discovery: " + asker + " asked (version " + version + "); answered with " + reply.Printers.Count
                         + " printer(s) as " + reply.Ip + ".");
            }
            catch (ObjectDisposedException)
            {
                // Stop() closed the socket while an answer was on its way: nothing to report.
            }
            catch (Exception ex)
            {
                Log.Warn("Discovery: could not answer " + asker + ": " + ex.Message);
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    internal static class UdpSockets
    {
        private const int SioUdpConnReset = -1744830452;   // SIO_UDP_CONNRESET

        /// <summary>
        /// On Windows a UDP socket reports ICMP "port unreachable" from an earlier send as an error on
        /// the next receive, which would kill a receive loop. Turn that off; other systems ignore it.
        /// </summary>
        public static void IgnoreConnectionResets(UdpClient socket)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
            try
            {
                socket.Client.IOControl(SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
            }
            catch (Exception ex)
            {
                Log.Warn("Could not set SIO_UDP_CONNRESET: " + ex.Message);
            }
        }
    }
}
