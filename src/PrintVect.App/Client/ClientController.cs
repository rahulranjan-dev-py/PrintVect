using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core.Client;
using PrintVect.Core.Config;
using PrintVect.Core.Discovery;
using PrintVect.Core.Elevation;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.App.Client
{
    /// <summary>
    /// Glue between the Use tab and the Core client service: finds hosts (UDP discovery while the
    /// Use tab is open, or a typed name), adds and removes virtual printers through
    /// PrintVect.Elevate.exe, keeps config.json and the job history in step, follows a host that
    /// changed its address, and re-raises events on the UI thread.
    /// </summary>
    internal sealed class ClientController : IDisposable
    {
        public const string TestPageFileName = "PrintVect-test-page.xps";

        private readonly AppPaths _paths;
        private readonly ConfigStore _store;
        private readonly AppConfig _config;
        private readonly SynchronizationContext _ui;
        private readonly ClientService _service;
        private readonly DiscoveryClient _discovery;
        private readonly JobHistoryStore<ClientJobRecord> _history;

        public ClientController(AppPaths paths, ConfigStore store, AppConfig config, SynchronizationContext ui)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            _paths = paths;
            _store = store;
            _config = config;
            _ui = ui;

            var tracker = new ClientJobTracker();
            _history = new JobHistoryStore<ClientJobRecord>(paths.ClientHistoryFile, () => tracker.All());
            tracker.Restore(_history.Load());
            tracker.Changed += (s, record) => _history.QueueSave();

            _service = new ClientService(paths, new TcpJobSender(), tracker)
            {
                Pin = config.Pin,
                KeepSentFilesHours = config.KeepSentFilesHours
            };
            _service.Jobs.Changed += (s, record) => Post(() => Raise(JobChanged, record));
            _service.JobFinished += (s, record) => Post(() => Raise(JobFinished, record));

            _discovery = new DiscoveryClient(config.DiscoveryPort);
            _discovery.HostsChanged += (s, e) => Post(OnHostsChanged);
        }

        public ClientJobTracker Jobs
        {
            get { return _service.Jobs; }
        }

        public string ConfigPath
        {
            get { return _store.Path; }
        }

        /// <summary>Plain-language reason a watcher could not start, or null.</summary>
        public string LastError { get; private set; }

        /// <summary>Raised on the UI thread for every change of a job.</summary>
        public event EventHandler<ClientJobRecord> JobChanged;

        /// <summary>Raised on the UI thread when a job is printed, failed or left waiting.</summary>
        public event EventHandler<ClientJobRecord> JobFinished;

        /// <summary>Raised on the UI thread after a printer was added or removed.</summary>
        public event EventHandler PrintersChanged;

        /// <summary>Raised on the UI thread when the list of hosts found in the office changed.</summary>
        public event EventHandler FoundChanged;

        /// <summary>Hosts that answered discovery or were looked up by hand, with their printers.</summary>
        public IList<DiscoveredHost> FoundHosts
        {
            get { return _discovery.Hosts; }
        }

        public bool IsDiscovering
        {
            get { return _discovery.IsRunning; }
        }

        /// <summary>The Use tab calls this when it is shown or hidden: discovery only runs while it is visible.</summary>
        public void SetDiscoveryActive(bool active)
        {
            if (active) _discovery.Start();
            else _discovery.Stop();
        }

        public IList<RemotePrinter> Printers
        {
            get { return _config.RemotePrinters.ToList(); }
        }

        public RemotePrinter FindPrinter(string printerId)
        {
            return _config.RemotePrinters.FirstOrDefault(p => string.Equals(p.PrinterId, printerId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>At start-up: watch the spool folder of every printer added earlier.</summary>
        public void StartIfConfigured()
        {
            foreach (RemotePrinter printer in _config.RemotePrinters.ToList())
            {
                try
                {
                    _service.StartWatching(printer);
                }
                catch (Exception ex)
                {
                    LastError = string.Format(Strings.UseWatcherError, printer.LocalPrinterName, ex.Message);
                    Log.Error("Could not watch for jobs for \"" + printer.LocalPrinterName + "\".", ex);
                }
            }
            if (_config.RemotePrinters.Count > 0)
            {
                Task.Run(() => _service.SweepOldFiles());
            }
        }

        /// <summary>Asks a host PC for its printer list (the "list" request over TCP).</summary>
        public async Task<ListReply> LookupHostAsync(string hostOrIp, CancellationToken ct)
        {
            string address = (hostOrIp ?? "").Trim();
            if (address.Length == 0) throw new ArgumentException("A PC name or IP address is required.", nameof(hostOrIp));
            Log.Info("Looking up the printers of " + address + ".");
            ListReply reply = await new JobClient(address, _config.JobPort).ListPrintersAsync(ct).ConfigureAwait(true);
            _discovery.AddManual(reply, address);
            return reply;
        }

        /// <summary>On the UI thread: a host appeared, changed or left. Follows hosts whose address changed (DHCP).</summary>
        private void OnHostsChanged()
        {
            bool printersChanged = false;
            foreach (DiscoveredHost host in _discovery.Hosts)
            {
                if (string.IsNullOrEmpty(host.Ip)) continue;
                foreach (RemotePrinter printer in _config.RemotePrinters)
                {
                    bool sameHost = host.Printers.Any(p => string.Equals(p.Id, printer.PrinterId, StringComparison.OrdinalIgnoreCase))
                                    || string.Equals(host.Host, printer.HostName, StringComparison.OrdinalIgnoreCase);
                    if (!sameHost) continue;
                    bool moved = !string.Equals(printer.HostIp, host.Ip, StringComparison.OrdinalIgnoreCase)
                                 || (host.Port > 0 && printer.Port != host.Port);
                    if (!moved) continue;
                    Log.Info("Host " + host.Host + " for \"" + printer.LocalPrinterName + "\" now answers at " + host.Ip + ":" + host.Port
                             + " (was " + printer.HostIp + ":" + printer.Port + "); updating config.json.");
                    printer.HostIp = host.Ip;
                    if (host.Port > 0) printer.Port = host.Port;
                    if (!string.IsNullOrWhiteSpace(host.Host)) printer.HostName = host.Host;
                    printersChanged = true;
                    try { _service.StartWatching(printer); }
                    catch (Exception ex) { Log.Warn("Watcher of \"" + printer.LocalPrinterName + "\" could not take the new address: " + ex.Message); }
                }
            }
            if (printersChanged)
            {
                TrySave();
                Raise(PrintersChanged);
            }
            Raise(FoundChanged);
        }

        /// <summary>Creates the virtual printer (UAC prompt) and remembers it. The result's Message is for the user.</summary>
        public async Task<ElevateResult> AddPrinterAsync(ListReply host, PrinterInfo printer, string typedAddress)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (printer == null) throw new ArgumentNullException(nameof(printer));

            RemotePrinter existing = FindPrinter(printer.Id);
            if (existing != null)
            {
                return new ElevateResult { Command = ElevateCommands.AddPrinter, Ok = true, ExitCode = ElevateExitCodes.Ok,
                    Message = string.Format(Strings.UseAlreadyAdded, existing.LocalPrinterName) };
            }

            string hostName = string.IsNullOrWhiteSpace(host.Host) ? typedAddress : host.Host.Trim();
            string hostIp = ChooseAddress(typedAddress, host.Ip);
            string friendly = string.IsNullOrWhiteSpace(printer.Friendly) ? printer.Name : printer.Friendly.Trim();
            string localName = ClientPrinterNames.PrinterName(friendly, hostName);
            string folder = ClientPrinterNames.SpoolFolder(_paths, printer.Id);
            string port = ClientPrinterNames.PortFile(_paths, printer.Id);

            var arguments = new ElevateArguments { Command = ElevateCommands.AddPrinter };
            arguments.Options[ElevateCommands.OptionId] = printer.Id;
            arguments.Options[ElevateCommands.OptionName] = localName;
            arguments.Options[ElevateCommands.OptionFolder] = folder;
            arguments.Options[ElevateCommands.OptionPort] = port;

            Log.Info("Adding printer \"" + friendly + "\" of " + hostName + " (" + hostIp + ") as \"" + localName + "\", id " + printer.Id + ".");
            ElevateResult result = await ElevateLauncher.RunAsync(_paths, arguments).ConfigureAwait(true);
            if (!result.Ok)
            {
                return result;
            }

            var remote = new RemotePrinter
            {
                PrinterId = printer.Id,
                HostName = hostName,
                HostIp = hostIp,
                Port = host.Port > 0 ? host.Port : _config.JobPort,
                LocalPrinterName = localName,
                FriendlyName = friendly
            };
            _config.RemotePrinters.Add(remote);
            if (!TrySave())
            {
                result.Message += " " + string.Format(Strings.SharingSaveError, _store.Path);
            }
            try
            {
                _service.StartWatching(remote);
            }
            catch (Exception ex)
            {
                Log.Error("The printer was created but its folder cannot be watched.", ex);
                result.Ok = false;
                result.Message = string.Format(Strings.UseWatcherError, localName, ex.Message);
            }
            Raise(PrintersChanged);
            return result;
        }

        /// <summary>Deletes the virtual printer, its port and folder (UAC prompt) and forgets it.</summary>
        public async Task<ElevateResult> RemovePrinterAsync(RemotePrinter printer)
        {
            if (printer == null) throw new ArgumentNullException(nameof(printer));
            _service.StopWatching(printer.PrinterId);

            var arguments = new ElevateArguments { Command = ElevateCommands.RemovePrinter };
            arguments.Options[ElevateCommands.OptionName] = printer.LocalPrinterName;
            arguments.Options[ElevateCommands.OptionPort] = ClientPrinterNames.PortFile(_paths, printer.PrinterId);
            arguments.Options[ElevateCommands.OptionFolder] = ClientPrinterNames.SpoolFolder(_paths, printer.PrinterId);

            Log.Info("Removing printer \"" + printer.LocalPrinterName + "\" (id " + printer.PrinterId + ").");
            ElevateResult result = await ElevateLauncher.RunAsync(_paths, arguments).ConfigureAwait(true);
            if (!result.Ok)
            {
                try { _service.StartWatching(printer); }
                catch (Exception ex) { Log.Warn("Watching could not resume after a failed removal: " + ex.Message); }
                return result;
            }

            _config.RemotePrinters.RemoveAll(p => string.Equals(p.PrinterId, printer.PrinterId, StringComparison.OrdinalIgnoreCase));
            if (!TrySave())
            {
                result.Message += " " + string.Format(Strings.SharingSaveError, _store.Path);
            }
            Raise(PrintersChanged);
            return result;
        }

        /// <summary>Drops the sample page into the printer's spool folder; it is sent like a real job.</summary>
        public void SendTestPage(RemotePrinter printer)
        {
            if (printer == null) throw new ArgumentNullException(nameof(printer));
            string sample = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, TestPageFileName);
            if (!File.Exists(sample))
            {
                throw new FileNotFoundException(string.Format(Strings.UseTestPageMissing, sample), sample);
            }
            _service.SendFile(printer.PrinterId, sample);
        }

        public int PendingCount(RemotePrinter printer)
        {
            try
            {
                return _service.PendingCount(printer.PrinterId);
            }
            catch (Exception ex)
            {
                Log.Warn("Waiting jobs of \"" + printer.LocalPrinterName + "\" could not be counted: " + ex.Message);
                return 0;
            }
        }

        public int RetryPending(RemotePrinter printer)
        {
            return _service.RetryPending(printer.PrinterId);
        }

        public IEnumerable<string> DescribeForDiagnostics()
        {
            var lines = new List<string>();
            lines.Add("Elevate helper: " + (File.Exists(ElevateLauncher.DefaultExePath()) ? "present" : "MISSING") + " (" + ElevateLauncher.DefaultExePath() + ")");
            lines.Add("Discovery: " + (_discovery.IsRunning ? "looking every " + _discovery.Interval.TotalSeconds + " s on UDP port " + _config.DiscoveryPort : "idle (runs while the Use tab is open)"));
            IList<DiscoveredHost> found = _discovery.Hosts;
            lines.Add("Hosts found: " + (found.Count == 0 ? "(none)" : string.Join("; ", found.Select(h => h.Host + " at " + h.Ip + ":" + h.Port
                + " with " + h.Printers.Count + " printer(s)" + (h.Manual ? ", typed" : ", seen " + h.LastSeen.ToString("HH:mm:ss", CultureInfo.InvariantCulture))))));
            lines.Add("Job history file: " + _history.Path);
            if (_config.RemotePrinters.Count == 0)
            {
                lines.Add("Printers from other PCs: (none)");
            }
            foreach (RemotePrinter printer in _config.RemotePrinters)
            {
                lines.Add("\"" + printer.LocalPrinterName + "\" = \"" + printer.FriendlyName + "\" on " + printer.HostName + " (" + printer.HostIp + ":" + printer.Port
                          + ") [id " + printer.PrinterId + "], folder " + ClientPrinterNames.SpoolFolder(_paths, printer.PrinterId)
                          + ", waiting jobs: " + PendingCount(printer));
            }
            if (LastError != null)
            {
                lines.Add("Last problem: " + LastError);
            }
            lines.Add("Jobs today: printed " + Jobs.CountToday(ClientJobStates.Printed) + ", failed " + Jobs.CountToday(ClientJobStates.Error)
                      + ", waiting " + Jobs.CountToday(ClientJobStates.Pending));
            foreach (ClientJobRecord job in Jobs.Recent(10))
            {
                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0:HH:mm:ss}  {1}  \"{2}\" -> {3} on {4}  {5}: {6}",
                    job.StartedAt, job.JobId, job.Doc, job.PrinterFriendly, job.HostName, job.State, job.Message));
            }
            return lines;
        }

        private static string ChooseAddress(string typed, string reported)
        {
            IPAddress ip;
            string t = (typed ?? "").Trim();
            if (t.Length > 0 && IPAddress.TryParse(t, out ip))
            {
                return t;
            }
            return string.IsNullOrWhiteSpace(reported) ? t : reported.Trim();
        }

        private bool TrySave()
        {
            try
            {
                _store.Save(_config);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Could not save " + _store.Path, ex);
                return false;
            }
        }

        private void Post(Action action)
        {
            _ui.Post(_ => action(), null);
        }

        private void Raise(EventHandler<ClientJobRecord> handler, ClientJobRecord record)
        {
            if (handler == null) return;
            try { handler(this, record); }
            catch (Exception ex) { Log.Error(record == null ? null : record.JobId, "A UI job listener failed.", ex); }
        }

        private void Raise(EventHandler handler)
        {
            if (handler == null) return;
            try { handler(this, EventArgs.Empty); }
            catch (Exception ex) { Log.Error("A UI printer listener failed.", ex); }
        }

        public void Dispose()
        {
            _discovery.Dispose();
            _service.Dispose();
            _history.Dispose();
        }
    }
}
