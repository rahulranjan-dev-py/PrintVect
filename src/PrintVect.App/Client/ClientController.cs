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
using PrintVect.Core.Elevation;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.App.Client
{
    /// <summary>
    /// Glue between the Use tab and the Core client service: looks up hosts, adds and removes
    /// virtual printers through PrintVect.Elevate.exe, keeps config.json in step, and re-raises
    /// job events on the UI thread.
    /// </summary>
    internal sealed class ClientController : IDisposable
    {
        public const string TestPageFileName = "PrintVect-test-page.xps";

        private readonly AppPaths _paths;
        private readonly ConfigStore _store;
        private readonly AppConfig _config;
        private readonly SynchronizationContext _ui;
        private readonly ClientService _service;

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

            _service = new ClientService(paths, new TcpJobSender(), new ClientJobTracker())
            {
                Pin = config.Pin,
                KeepSentFilesHours = config.KeepSentFilesHours
            };
            _service.Jobs.Changed += (s, record) => Post(() => Raise(JobChanged, record));
            _service.JobFinished += (s, record) => Post(() => Raise(JobFinished, record));
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
        public Task<ListReply> LookupHostAsync(string hostOrIp, CancellationToken ct)
        {
            string address = (hostOrIp ?? "").Trim();
            if (address.Length == 0) throw new ArgumentException("A PC name or IP address is required.", nameof(hostOrIp));
            Log.Info("Looking up the printers of " + address + ".");
            return new JobClient(address, _config.JobPort).ListPrintersAsync(ct);
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
            _service.Dispose();
        }
    }
}
