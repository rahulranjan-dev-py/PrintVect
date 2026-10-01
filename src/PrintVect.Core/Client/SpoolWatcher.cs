using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Client
{
    /// <summary>A file the watcher has claimed: renamed to job-{guid}.{xps|oxps} and ready to send.</summary>
    public sealed class SpoolFileReadyEventArgs : EventArgs
    {
        public string JobId { get; set; }
        public string FilePath { get; set; }
        public string Format { get; set; }
        public string Title { get; set; }
        public long Size { get; set; }
    }

    /// <summary>
    /// Watches one remote printer's spool folder for the file the Local Port writes (brief 5.2):
    /// FileSystemWatcher plus a 2-second polling fallback. A file is taken when it can be opened
    /// exclusively and its size has not changed for one second; it is renamed to job-{guid}.xps
    /// at once so the next print cannot overwrite it, inspected, and announced through FileReady.
    /// Files that are not XPS packages go to failed\ with a log line instead of being sent.
    /// </summary>
    public sealed class SpoolWatcher : IDisposable
    {
        public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan DefaultStableFor = TimeSpan.FromSeconds(1);
        private const string JobPrefix = "job-";

        private readonly string _folder;
        private readonly string _printerLabel;
        private readonly object _gate = new object();
        private readonly Dictionary<string, Seen> _seen = new Dictionary<string, Seen>(StringComparer.OrdinalIgnoreCase);
        private FileSystemWatcher _watcher;
        private Timer _poll;
        private bool _scanning;
        private bool _disposed;

        public SpoolWatcher(string folder, string printerLabel)
        {
            if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("A folder is required.", nameof(folder));
            _folder = folder;
            _printerLabel = printerLabel ?? folder;
            StableFor = DefaultStableFor;
        }

        public string Folder
        {
            get { return _folder; }
        }

        /// <summary>How long a file's size must stay unchanged (tests shorten it).</summary>
        public TimeSpan StableFor { get; set; }

        /// <summary>Raised on a thread-pool thread for each file taken from the folder.</summary>
        public event EventHandler<SpoolFileReadyEventArgs> FileReady;

        public void Start()
        {
            Directory.CreateDirectory(_folder);
            try
            {
                _watcher = new FileSystemWatcher(_folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
                };
                _watcher.Created += (s, e) => Scan("file created");
                _watcher.Changed += (s, e) => Scan("file changed");
                _watcher.Renamed += (s, e) => Scan("file renamed");
                _watcher.Error += (s, e) => Log.Warn("Spool watcher for \"" + _printerLabel + "\" lost events (" + e.GetException().Message + "); polling continues.");
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                Log.Warn("Spool watcher for \"" + _printerLabel + "\" could not watch " + _folder + " (" + ex.Message + "); polling every "
                         + PollInterval.TotalSeconds + " s instead.");
                _watcher = null;
            }
            _poll = new Timer(_ => Scan(null), null, PollInterval, PollInterval);
            Log.Info("Watching " + _folder + " for print jobs for \"" + _printerLabel + "\".");
        }

        /// <summary>Looks at the folder now (also used by tests and by the Retry button).</summary>
        public void Scan(string reason)
        {
            lock (_gate)
            {
                if (_scanning || _disposed) return;
                _scanning = true;
            }
            try
            {
                ScanOnce();
            }
            catch (Exception ex)
            {
                Log.Error("Spool watcher for \"" + _printerLabel + "\" failed while scanning " + _folder + ".", ex);
            }
            finally
            {
                lock (_gate)
                {
                    _scanning = false;
                }
            }
        }

        private void ScanOnce()
        {
            if (!Directory.Exists(_folder)) return;
            foreach (string path in Directory.GetFiles(_folder))
            {
                string name = Path.GetFileName(path);
                if (name.StartsWith(JobPrefix, StringComparison.OrdinalIgnoreCase)) continue;   // already claimed, being sent
                if (name.StartsWith(".", StringComparison.Ordinal) || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

                if (IsComplete(path))
                {
                    Claim(path);
                }
            }
            lock (_gate)
            {
                var stale = new List<string>();
                foreach (KeyValuePair<string, Seen> pair in _seen)
                {
                    if (!File.Exists(pair.Key)) stale.Add(pair.Key);
                }
                foreach (string key in stale) _seen.Remove(key);
            }
        }

        /// <summary>True when the writer has closed the file and its size has not moved for StableFor.</summary>
        private bool IsComplete(string path)
        {
            long size;
            try
            {
                size = new FileInfo(path).Length;
            }
            catch (IOException)
            {
                return false;
            }
            if (size <= 0) return false;

            DateTime now = DateTime.UtcNow;
            lock (_gate)
            {
                Seen seen;
                if (!_seen.TryGetValue(path, out seen) || seen.Size != size)
                {
                    _seen[path] = new Seen { Size = size, Since = now };
                    return false;
                }
                if (now - seen.Since < StableFor) return false;
            }

            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                }
                return true;
            }
            catch (IOException)
            {
                return false;   // the spooler still has it open
            }
            catch (UnauthorizedAccessException ex)
            {
                Log.Warn("Print file " + path + " cannot be opened for sending (" + ex.Message + "). Check the folder permissions.");
                return false;
            }
        }

        private void Claim(string path)
        {
            string jobId = Guid.NewGuid().ToString("D");
            XpsFileInfo info = XpsFormatSniffer.Inspect(path);
            lock (_gate)
            {
                _seen.Remove(path);
            }

            if (!info.IsXpsPackage)
            {
                string failed = Path.Combine(_folder, ClientPrinterNames.FailedFolderName, JobPrefix + jobId + Path.GetExtension(path));
                Log.Warn(jobId, "The file " + path + " is not something PrintVect can print (" + info.Problem + "); moved to " + failed + ".");
                TryMove(path, failed, jobId);
                return;
            }

            string target = Path.Combine(_folder, JobPrefix + jobId + "." + XpsFormatSniffer.ExtensionFor(info.Format));
            if (!TryMove(path, target, jobId)) return;

            long size = new FileInfo(target).Length;
            Log.Info(jobId, string.Format("New print job for \"{0}\": {1} ({2:N0} bytes, {3}{4}) renamed to {5}.",
                _printerLabel, Path.GetFileName(path), size, info.Format,
                info.Title == null ? "" : ", title \"" + info.Title + "\"", Path.GetFileName(target)));

            EventHandler<SpoolFileReadyEventArgs> handler = FileReady;
            if (handler == null) return;
            try
            {
                handler(this, new SpoolFileReadyEventArgs { JobId = jobId, FilePath = target, Format = info.Format, Title = info.Title, Size = size });
            }
            catch (Exception ex)
            {
                Log.Error(jobId, "The job could not be handed over for sending.", ex);
            }
        }

        private static bool TryMove(string from, string to, string jobId)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                if (File.Exists(to)) File.Delete(to);
                File.Move(from, to);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(jobId, "Could not rename " + from + " to " + to + " (" + ex.Message + "); will try again.");
                return false;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            if (_watcher != null)
            {
                try { _watcher.EnableRaisingEvents = false; _watcher.Dispose(); }
                catch (Exception ex) { Log.Warn("Spool watcher for \"" + _printerLabel + "\" did not close cleanly: " + ex.Message); }
                _watcher = null;
            }
            if (_poll != null)
            {
                _poll.Dispose();
                _poll = null;
            }
        }

        private sealed class Seen
        {
            public long Size;
            public DateTime Since;
        }
    }
}
