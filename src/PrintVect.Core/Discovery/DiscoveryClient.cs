using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Discovery
{
    /// <summary>A host PC that answered discovery (or was looked up by hand) and the printers it shares.</summary>
    public sealed class DiscoveredHost
    {
        public string Host { get; set; }
        public string Ip { get; set; }
        public int Port { get; set; }
        public List<PrinterInfo> Printers { get; set; } = new List<PrinterInfo>();
        public DateTime LastSeen { get; set; }
        /// <summary>Entered by hand (Look up); never expires.</summary>
        public bool Manual { get; set; }
        /// <summary>Which of this PC's addresses heard the answer.</summary>
        public string Via { get; set; }

        public DiscoveredHost Clone()
        {
            var copy = (DiscoveredHost)MemberwiseClone();
            copy.Printers = Printers.Select(p => new PrinterInfo { Id = p.Id, Name = p.Name, Friendly = p.Friendly, Status = p.Status }).ToList();
            return copy;
        }

        public ListReply ToListReply()
        {
            return new ListReply { Host = Host, Ip = Ip, Port = Port, Printers = Printers.ToList() };
        }
    }

    /// <summary>
    /// Client side of discovery (brief, section 6): every 10 seconds while the Use tab is open, send
    /// "PVECT-DISCOVER 1" out of every network card (to that card's broadcast address and to
    /// 255.255.255.255), collect the unicast answers, and forget a host that has not answered for
    /// 30 seconds. Hosts typed by hand are kept alongside.
    /// </summary>
    public sealed class DiscoveryClient : IDisposable
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan DefaultExpiry = TimeSpan.FromSeconds(30);

        private readonly int _port;
        private readonly object _gate = new object();
        private readonly Dictionary<string, DiscoveredHost> _hosts = new Dictionary<string, DiscoveredHost>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Listener> _listeners = new Dictionary<string, Listener>();
        private Timer _timer;
        private bool _running;
        private bool _disposed;

        public DiscoveryClient(int port)
        {
            _port = port;
        }

        public TimeSpan Interval { get; set; } = DefaultInterval;
        public TimeSpan Expiry { get; set; } = DefaultExpiry;

        public bool IsRunning
        {
            get { lock (_gate) { return _running; } }
        }

        /// <summary>Raised on a thread-pool thread when a host appears, changes or disappears.</summary>
        public event EventHandler HostsChanged;

        /// <summary>Snapshot, hosts in name order.</summary>
        public IList<DiscoveredHost> Hosts
        {
            get { lock (_gate) { return _hosts.Values.OrderBy(h => h.Host, StringComparer.OrdinalIgnoreCase).Select(h => h.Clone()).ToList(); } }
        }

        public void Start()
        {
            lock (_gate)
            {
                if (_disposed || _running) return;
                _running = true;
                _timer = new Timer(_ => Cycle(), null, TimeSpan.Zero, Interval);
            }
            Log.Info("Discovery: looking for host PCs every " + Interval.TotalSeconds + " s on UDP port " + _port + ".");
        }

        public void Stop()
        {
            List<Listener> listeners;
            lock (_gate)
            {
                if (!_running) return;
                _running = false;
                if (_timer != null) { _timer.Dispose(); _timer = null; }
                listeners = _listeners.Values.ToList();
                _listeners.Clear();
            }
            foreach (Listener listener in listeners) listener.Dispose();
            Log.Info("Discovery: stopped looking for host PCs.");
        }

        /// <summary>Sends one round now (also what the timer does).</summary>
        public void Cycle()
        {
            try
            {
                RefreshListeners();
                List<Listener> listeners;
                lock (_gate) { listeners = _listeners.Values.ToList(); }
                foreach (Listener listener in listeners)
                {
                    listener.SendRequest(_port);
                }
                ExpireOldHosts();
            }
            catch (Exception ex)
            {
                Log.Error("Discovery round failed.", ex);
            }
        }

        /// <summary>Remembers a host looked up by hand so it shows next to the discovered ones.</summary>
        public void AddManual(ListReply reply, string address)
        {
            if (reply == null) throw new ArgumentNullException(nameof(reply));
            string name = string.IsNullOrWhiteSpace(reply.Host) ? address : reply.Host.Trim();
            string ip = ChooseIp(address, reply.Ip);
            Upsert(name, ip, reply, "typed", true);
        }

        public void Forget(string hostName)
        {
            bool removed;
            lock (_gate) { removed = _hosts.Remove(hostName ?? ""); }
            if (removed) RaiseChanged();
        }

        /// <summary>Asks one address directly (unicast); used for tests and for re-checking a known host.</summary>
        public async Task<ListReply> ProbeAsync(IPEndPoint target, TimeSpan timeout)
        {
            using (var socket = new UdpClient(new IPEndPoint(IPAddress.Any, 0)))
            {
                UdpSockets.IgnoreConnectionResets(socket);
                await socket.SendAsync(DiscoveryMessages.RequestBytes, DiscoveryMessages.RequestBytes.Length, target).ConfigureAwait(false);
                Task<UdpReceiveResult> receive = socket.ReceiveAsync();
                Task finished = await Task.WhenAny(receive, Task.Delay(timeout)).ConfigureAwait(false);
                if (finished != receive)
                {
                    socket.Close();
                    return null;
                }
                UdpReceiveResult result = await receive.ConfigureAwait(false);
                ListReply reply = DiscoveryMessages.ParseReply(result.Buffer);
                if (reply != null)
                {
                    Upsert(reply.Host, result.RemoteEndPoint.Address.ToString(), reply, "probe", false);
                }
                return reply;
            }
        }

        private void RefreshListeners()
        {
            IList<LocalNetwork> networks = LocalNetworks.List();
            var wanted = networks.ToDictionary(n => n.Address.ToString(), n => n);
            var toClose = new List<Listener>();
            var toOpen = new List<LocalNetwork>();
            lock (_gate)
            {
                if (!_running) return;
                foreach (KeyValuePair<string, Listener> pair in _listeners.ToList())
                {
                    if (!wanted.ContainsKey(pair.Key))
                    {
                        toClose.Add(pair.Value);
                        _listeners.Remove(pair.Key);
                    }
                }
                foreach (KeyValuePair<string, LocalNetwork> pair in wanted)
                {
                    if (!_listeners.ContainsKey(pair.Key)) toOpen.Add(pair.Value);
                }
            }
            foreach (Listener listener in toClose)
            {
                Log.Info("Discovery: no longer using " + listener.Network + ".");
                listener.Dispose();
            }
            foreach (LocalNetwork network in toOpen)
            {
                try
                {
                    var listener = new Listener(network, OnReply);
                    lock (_gate)
                    {
                        if (!_running) { listener.Dispose(); return; }
                        _listeners[network.Address.ToString()] = listener;
                    }
                    Log.Info("Discovery: asking on " + network + ", broadcast " + network.Broadcast + ".");
                }
                catch (Exception ex)
                {
                    Log.Warn("Discovery: cannot use " + network + ": " + ex.Message);
                }
            }
        }

        private void OnReply(byte[] data, IPEndPoint from, LocalNetwork via)
        {
            ListReply reply = DiscoveryMessages.ParseReply(data);
            if (reply == null) return;
            Upsert(reply.Host, from.Address.ToString(), reply, via.Address.ToString(), false);
        }

        private void Upsert(string hostName, string ip, ListReply reply, string via, bool manual)
        {
            string key = string.IsNullOrWhiteSpace(hostName) ? ip : hostName.Trim();
            bool changed;
            lock (_gate)
            {
                DiscoveredHost host;
                if (!_hosts.TryGetValue(key, out host))
                {
                    host = new DiscoveredHost { Host = key };
                    _hosts[key] = host;
                    changed = true;
                    Log.Info("Discovery: found " + key + " at " + ip + " with " + reply.Printers.Count + " printer(s) (" + via + ").");
                }
                else
                {
                    changed = host.Ip != ip || host.Port != reply.Port || !SamePrinters(host.Printers, reply.Printers);
                    if (host.Ip != ip) Log.Info("Discovery: " + key + " now answers from " + ip + " (was " + host.Ip + ").");
                }
                host.Ip = ip;
                host.Port = reply.Port > 0 ? reply.Port : host.Port;
                host.Printers = reply.Printers.Select(p => new PrinterInfo { Id = p.Id, Name = p.Name, Friendly = p.Friendly, Status = p.Status }).ToList();
                host.LastSeen = DateTime.Now;
                host.Manual = host.Manual || manual;
                host.Via = via;
            }
            if (changed) RaiseChanged();
        }

        private void ExpireOldHosts()
        {
            var gone = new List<string>();
            lock (_gate)
            {
                DateTime cutoff = DateTime.Now - Expiry;
                foreach (DiscoveredHost host in _hosts.Values.ToList())
                {
                    if (!host.Manual && host.LastSeen < cutoff)
                    {
                        gone.Add(host.Host);
                        _hosts.Remove(host.Host);
                    }
                }
            }
            if (gone.Count == 0) return;
            foreach (string name in gone) Log.Info("Discovery: " + name + " has not answered for " + Expiry.TotalSeconds + " s; removed from the list.");
            RaiseChanged();
        }

        private static bool SamePrinters(List<PrinterInfo> a, List<PrinterInfo> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Id != b[i].Id || a[i].Friendly != b[i].Friendly || a[i].Status != b[i].Status || a[i].Name != b[i].Name) return false;
            }
            return true;
        }

        private static string ChooseIp(string typed, string reported)
        {
            IPAddress ip;
            string t = (typed ?? "").Trim();
            if (t.Length > 0 && IPAddress.TryParse(t, out ip)) return t;
            return string.IsNullOrWhiteSpace(reported) ? t : reported.Trim();
        }

        private void RaiseChanged()
        {
            EventHandler handler = HostsChanged;
            if (handler == null) return;
            try { handler(this, EventArgs.Empty); }
            catch (Exception ex) { Log.Error("A discovery listener failed.", ex); }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            Stop();
        }

        /// <summary>One UDP socket bound to one of this PC's addresses: sends the broadcasts and hears the answers.</summary>
        private sealed class Listener : IDisposable
        {
            private readonly UdpClient _socket;
            private readonly Action<byte[], IPEndPoint, LocalNetwork> _onReply;
            private bool _disposed;

            public Listener(LocalNetwork network, Action<byte[], IPEndPoint, LocalNetwork> onReply)
            {
                Network = network;
                _onReply = onReply;
                _socket = new UdpClient(new IPEndPoint(network.Address, 0)) { EnableBroadcast = true };
                UdpSockets.IgnoreConnectionResets(_socket);
                Task.Run(() => ReceiveLoopAsync());
            }

            public LocalNetwork Network { get; }

            public void SendRequest(int port)
            {
                byte[] request = DiscoveryMessages.RequestBytes;
                foreach (IPAddress target in new[] { Network.Broadcast, IPAddress.Broadcast })
                {
                    try
                    {
                        _socket.Send(request, request.Length, new IPEndPoint(target, port));
                    }
                    catch (Exception ex)
                    {
                        if (_disposed) return;
                        Log.Warn("Discovery: sending to " + target + " from " + Network.Address + " failed: " + ex.Message);
                    }
                }
            }

            private async Task ReceiveLoopAsync()
            {
                while (!_disposed)
                {
                    UdpReceiveResult received;
                    try
                    {
                        received = await _socket.ReceiveAsync().ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (SocketException ex)
                    {
                        if (_disposed) return;
                        Log.Warn("Discovery receive on " + Network.Address + " failed (" + ex.SocketErrorCode + "); continuing.");
                        continue;
                    }
                    catch (Exception ex)
                    {
                        if (_disposed) return;
                        Log.Error("Discovery receive on " + Network.Address + " failed unexpectedly; continuing.", ex);
                        continue;
                    }
                    try
                    {
                        _onReply(received.Buffer, received.RemoteEndPoint, Network);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("A discovery answer from " + received.RemoteEndPoint + " could not be handled.", ex);
                    }
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                try { _socket.Close(); }
                catch (Exception ex) { Log.Warn("Discovery socket on " + Network.Address + " did not close cleanly: " + ex.Message); }
            }
        }
    }
}
