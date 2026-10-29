using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core.Config;
using PrintVect.Core.Diagnostics;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Client
{
    /// <summary>
    /// The light presence signal (M6b-2): every few minutes, this PC says hello to each host whose printers it has
    /// added, so that host can list the PCs that use it with their Windows and PrintVect versions. A host that does
    /// not answer is simply tried again next time; the log notes only changes, not every quiet round. A host that
    /// refuses the hello (a different sharing PIN, 0.5.8) gets no more hellos until this PC's PIN changes, so a wrong
    /// PIN does not keep counting as wrong tries there and make that host wait before it takes this PC's jobs.
    /// </summary>
    public sealed class PresenceAnnouncer : IDisposable
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan DefaultFirstDelay = TimeSpan.FromSeconds(15);

        private readonly IJobSender _sender;
        private readonly Func<IList<RemotePrinter>> _printers;
        private readonly Dictionary<string, bool> _lastAnswered = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _refused = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        private readonly Timer _timer;
        private int _running;
        private string _windows;
        private string _pin = "";

        public PresenceAnnouncer(IJobSender sender, Func<IList<RemotePrinter>> printers)
        {
            if (sender == null) throw new ArgumentNullException(nameof(sender));
            if (printers == null) throw new ArgumentNullException(nameof(printers));
            _sender = sender;
            _printers = printers;
            _timer = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public TimeSpan Interval { get; set; } = DefaultInterval;

        /// <summary>Raised on a thread-pool thread with one printer of a host that answered a hello (0.5.8: its key can be learned).</summary>
        public event EventHandler<RemotePrinter> HostAnswered;

        /// <summary>The sharing PIN each hello proves; a change lets refused hosts hear from this PC again.</summary>
        public string Pin
        {
            get { return Volatile.Read(ref _pin); }
            set
            {
                Volatile.Write(ref _pin, value ?? "");
                lock (_refused)
                {
                    _refused.Clear();
                }
            }
        }

        public void Start(TimeSpan firstDelay)
        {
            _timer.Change(firstDelay, Interval);
        }

        /// <summary>Says hello right away (after a printer was added), in the background.</summary>
        public void AnnounceNow()
        {
            Fire();
        }

        private void Fire()
        {
            if (_stopping.IsCancellationRequested || Interlocked.Exchange(ref _running, 1) == 1)
            {
                return;   // the previous round is still going (a host that takes its 5 s to time out)
            }
            Task.Run(async () =>
            {
                try
                {
                    await AnnounceAsync(_stopping.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // PrintVect is closing.
                }
                catch (Exception ex)
                {
                    Log.Error("Presence: a round of hellos failed.", ex);
                }
                finally
                {
                    Interlocked.Exchange(ref _running, 0);
                }
            });
        }

        /// <summary>One hello per host, listing the printers of that host added on this PC. Public for tests.</summary>
        public async Task AnnounceAsync(CancellationToken ct)
        {
            if (_windows == null)
            {
                _windows = WindowsInfo.Edition();
            }
            foreach (IGrouping<string, RemotePrinter> host in _printers().Where(p => p != null)
                         .GroupBy(p => string.IsNullOrWhiteSpace(p.HostName) ? p.HostIp ?? "" : p.HostName, StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                lock (_refused)
                {
                    if (_refused.Contains(host.Key)) continue;
                }
                RequestHeader header = RequestHeader.ForHello(_windows, AppInfo.Version, host.Select(p => p.PrinterId), Pin);
                bool answered;
                string detail;
                try
                {
                    JobReply reply = await _sender.HelloAsync(host.First(), header, ct).ConfigureAwait(false);
                    answered = reply != null && reply.Ok;
                    detail = reply == null ? "no answer" : reply.Message;
                    if (reply != null && !reply.Ok)
                    {
                        lock (_refused)
                        {
                            _refused.Add(host.Key);
                        }
                        Log.Info("Presence: " + host.Key + " refused the hello (" + reply.Message + "); no more hellos to it until the sharing PIN "
                                 + "changes on this PC or PrintVect starts again.");
                        continue;
                    }
                    if (answered)
                    {
                        RaiseHostAnswered(host.First());
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    answered = false;
                    detail = ex.Message;
                }
                NoteChange(host.Key, answered, detail);
            }
        }

        private void RaiseHostAnswered(RemotePrinter printer)
        {
            EventHandler<RemotePrinter> handler = HostAnswered;
            if (handler == null) return;
            try { handler(this, printer); }
            catch (Exception ex) { Log.Error("Presence: a listener for " + printer.HostName + "'s answer failed.", ex); }
        }

        private void NoteChange(string host, bool answered, string detail)
        {
            bool before;
            bool known;
            lock (_lastAnswered)
            {
                known = _lastAnswered.TryGetValue(host, out before);
                _lastAnswered[host] = answered;
            }
            if (!known || before != answered)
            {
                Log.Info("Presence: " + (answered ? "hello to " + host + " answered." : host + " did not take the hello (" + detail + ")."));
            }
        }

        public void Dispose()
        {
            _stopping.Cancel();
            _timer.Dispose();
        }
    }
}
