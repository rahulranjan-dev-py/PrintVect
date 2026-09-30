using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core.Config;
using PrintVect.Core.Host;
using PrintVect.Core.Logging;
using PrintVect.Core.Printing;
using PrintVect.Core.Protocol;

namespace PrintVect.App.Host
{
    /// <summary>
    /// Glue between the UI and the Core host service: turns sharing on and off, keeps
    /// config.json in step, and re-raises job events on the UI thread.
    /// </summary>
    internal sealed class HostController : IDisposable
    {
        private readonly ConfigStore _store;
        private readonly AppConfig _config;
        private readonly SynchronizationContext _ui;
        private readonly HostService _service;

        public HostController(AppPaths paths, ConfigStore store, AppConfig config, SynchronizationContext ui)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            _store = store;
            _config = config;
            _ui = ui;

            _service = new HostService(paths, new SystemPrintingEngine(), new LocalPrinters(), new JobTracker());
            _service.Pin = config.Pin;
            _service.UpdateSharedPrinters(config.SharedPrinters);
            _service.JobReceived += (s, record) => Post(() => Raise(JobReceived, record));
            _service.JobFinished += (s, record) => Post(() => Raise(JobFinished, record));
        }

        public JobTracker Jobs
        {
            get { return _service.Jobs; }
        }

        public bool IsSharing
        {
            get { return _service.IsListening; }
        }

        public int Port
        {
            get { return _service.IsListening ? _service.Port : _config.JobPort; }
        }

        public string ConfigPath
        {
            get { return _store.Path; }
        }

        /// <summary>Plain-language reason the last attempt to share failed, or null.</summary>
        public string LastError { get; private set; }

        /// <summary>Raised on the UI thread.</summary>
        public event EventHandler<JobRecord> JobReceived;

        /// <summary>Raised on the UI thread.</summary>
        public event EventHandler<JobRecord> JobFinished;

        /// <summary>Raised on the UI thread after sharing was turned on or off.</summary>
        public event EventHandler SharingChanged;

        public bool TrySetSharing(bool on, out string error)
        {
            error = null;
            if (on == IsSharing)
            {
                if (_config.SharingEnabled != on)
                {
                    _config.SharingEnabled = on;
                    TrySave();
                }
                return true;
            }

            if (on)
            {
                try
                {
                    _service.Pin = _config.Pin;
                    _service.UpdateSharedPrinters(_config.SharedPrinters);
                    _service.Start(_config.JobPort);
                }
                catch (SocketException ex)
                {
                    Log.Error("Could not open TCP port " + _config.JobPort + " for sharing.", ex);
                    error = string.Format(Strings.SharingPortError, _config.JobPort, _store.Path);
                    LastError = error;
                    return false;
                }
                catch (Exception ex)
                {
                    Log.Error("Sharing could not start.", ex);
                    error = string.Format(Strings.SharingStartError, ex.Message);
                    LastError = error;
                    return false;
                }
            }
            else
            {
                _service.Stop();
            }

            LastError = null;
            _config.SharingEnabled = on;
            TrySave();
            Raise(SharingChanged);
            return true;
        }

        /// <summary>At start-up: resume sharing if it was on last time.</summary>
        public void StartIfConfigured()
        {
            if (!_config.SharingEnabled)
            {
                return;
            }
            string error;
            if (!TrySetSharing(true, out error))
            {
                Log.Warn("Sharing stays OFF: " + error);
            }
        }

        public Task<IList<LocalPrinterInfo>> ListPrintersAsync()
        {
            return Task.Run(() => new LocalPrinters().GetPrinters());
        }

        public SharedPrinter FindShared(string localName)
        {
            return _config.SharedPrinters.FirstOrDefault(p => string.Equals(p.LocalName, localName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Shares or un-shares one printer and saves. Returns false when config.json could not be written.</summary>
        public bool SetShared(string localName, bool shared, string friendlyName)
        {
            SharedPrinter existing = FindShared(localName);
            if (shared)
            {
                if (existing == null)
                {
                    existing = new SharedPrinter { LocalName = localName };
                    _config.SharedPrinters.Add(existing);
                }
                existing.Id = PrinterIds.For(Environment.MachineName, localName);
                existing.FriendlyName = Clean(friendlyName, localName);
                Log.Info("Now sharing \"" + localName + "\" as \"" + existing.FriendlyName + "\" (id " + existing.Id + ").");
            }
            else if (existing != null)
            {
                _config.SharedPrinters.Remove(existing);
                Log.Info("No longer sharing \"" + localName + "\".");
            }
            _service.UpdateSharedPrinters(_config.SharedPrinters);
            return TrySave();
        }

        public bool SetFriendlyName(string localName, string friendlyName)
        {
            SharedPrinter existing = FindShared(localName);
            if (existing == null)
            {
                return true;
            }
            existing.FriendlyName = Clean(friendlyName, localName);
            Log.Info("Printer \"" + localName + "\" is now shown as \"" + existing.FriendlyName + "\".");
            _service.UpdateSharedPrinters(_config.SharedPrinters);
            return TrySave();
        }

        public IEnumerable<string> DescribeForDiagnostics()
        {
            var lines = new List<string>();
            lines.Add(IsSharing
                ? "Sharing: ON, listening on TCP port " + _service.Port + " since " + (_service.StartedAt ?? DateTime.Now).ToString("HH:mm:ss", CultureInfo.InvariantCulture)
                : "Sharing: OFF" + (LastError == null ? "" : " (last attempt failed: " + LastError + ")"));
            lines.Add("Shared printers: " + (_config.SharedPrinters.Count == 0
                ? "(none)"
                : string.Join("; ", _config.SharedPrinters.Select(p => "\"" + p.FriendlyName + "\" = " + p.LocalName + " [id " + p.Id + "]"))));
            lines.Add("Jobs today: received " + Jobs.CountReceivedToday() + ", printed " + Jobs.CountToday(JobStates.Printed)
                      + ", failed " + Jobs.CountToday(JobStates.Error) + "; waiting for the printer now: " + _service.PendingPrintCount);
            lines.Add("Received files folder: " + _service.IncomingDirectory);
            foreach (JobRecord job in Jobs.Recent(10))
            {
                lines.Add(string.Format(CultureInfo.InvariantCulture, "{0:HH:mm:ss}  {1}  from {2}  \"{3}\" -> {4}  {5}: {6}",
                    job.ReceivedAt, job.JobId, job.Client, job.Doc, job.PrinterFriendly, job.State, job.Message));
            }
            return lines;
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

        private static string Clean(string friendly, string fallback)
        {
            return string.IsNullOrWhiteSpace(friendly) ? fallback : friendly.Trim();
        }

        private void Post(Action action)
        {
            _ui.Post(_ => action(), null);
        }

        private void Raise(EventHandler<JobRecord> handler, JobRecord record)
        {
            if (handler == null) return;
            try { handler(this, record); }
            catch (Exception ex) { Log.Error(record == null ? null : record.JobId, "A UI job listener failed.", ex); }
        }

        private void Raise(EventHandler handler)
        {
            if (handler == null) return;
            try { handler(this, EventArgs.Empty); }
            catch (Exception ex) { Log.Error("A UI sharing listener failed.", ex); }
        }

        public void Dispose()
        {
            _service.Dispose();
        }
    }
}
