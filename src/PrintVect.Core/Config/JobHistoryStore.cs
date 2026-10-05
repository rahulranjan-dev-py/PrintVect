using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Config
{
    /// <summary>
    /// Keeps a job list on disk (jobs-host.json / jobs-client.json next to config.json) so the job
    /// lists and today's counts survive a restart (brief, M3). Saves at most once a second, to a
    /// temporary file first; a file that cannot be read is kept aside and the list starts empty.
    /// </summary>
    public sealed class JobHistoryStore<T> : IDisposable
    {
        public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        private readonly Func<IList<T>> _snapshot;
        private readonly object _gate = new object();
        private readonly Timer _timer;
        private bool _dirty;
        private bool _disposed;

        /// <param name="snapshot">Returns the records to save (called on a timer thread).</param>
        public JobHistoryStore(string path, Func<IList<T>> snapshot)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A path is required.", nameof(path));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            Path = path;
            _snapshot = snapshot;
            _timer = new Timer(_ => SaveIfDirty(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public string Path { get; }

        /// <summary>Never throws: a missing file gives an empty list, a bad one is renamed and gives an empty list.</summary>
        public IList<T> Load()
        {
            if (!File.Exists(Path)) return new List<T>();
            try
            {
                string json = File.ReadAllText(Path, Encoding.UTF8);
                IList<T> records = JsonConvert.DeserializeObject<List<T>>(json) ?? new List<T>();
                Log.Info("Job history: " + records.Count + " job(s) loaded from " + Path + ".");
                return records;
            }
            catch (Exception ex)
            {
                string backup = null;
                try
                {
                    backup = Path + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                    File.Move(Path, backup);
                }
                catch (Exception moveEx)
                {
                    Log.Warn("Job history: could not set the unreadable file aside: " + moveEx.Message);
                }
                Log.Error("Job history: " + Path + " could not be read; starting with an empty list."
                          + (backup == null ? "" : " The old file was kept as " + backup + "."), ex);
                return new List<T>();
            }
        }

        /// <summary>Call after every change; the save happens about a second later.</summary>
        public void QueueSave()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _dirty = true;
                _timer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>Saves now if anything changed (at exit).</summary>
        public void Flush()
        {
            SaveIfDirty();
        }

        private void SaveIfDirty()
        {
            lock (_gate)
            {
                if (!_dirty) return;
                _dirty = false;
            }
            try
            {
                IList<T> records = _snapshot();
                string json = JsonConvert.SerializeObject(records, Formatting.Indented);
                string folder = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                string temp = Path + ".tmp";
                File.WriteAllText(temp, json, Utf8NoBom);
                if (File.Exists(Path)) File.Delete(Path);
                File.Move(temp, Path);
            }
            catch (Exception ex)
            {
                Log.Warn("Job history: could not save " + Path + ": " + ex.Message);
                lock (_gate) { _dirty = true; }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _timer.Dispose();
            SaveIfDirty();
        }
    }
}
