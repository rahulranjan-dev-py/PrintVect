using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core.Config;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Client
{
    /// <summary>
    /// The client role (brief 5.2): one SpoolWatcher per remote printer, each new file sent to its
    /// host with three tries over 30 seconds. Sent files wait in sent\ for KeepSentFilesHours,
    /// refused or failed ones in failed\ (last 20 kept), unreachable-host ones in pending\ until
    /// Retry. Every step is logged with the job id so it can be matched with the host's log.
    /// </summary>
    public sealed class ClientService : IDisposable
    {
        public const int MaxFailedFiles = 20;
        public static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(10);

        private readonly AppPaths _paths;
        private readonly IJobSender _sender;
        private readonly object _gate = new object();
        private readonly Dictionary<string, Watched> _watched = new Dictionary<string, Watched>(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        private readonly Timer _sweep;
        private bool _disposed;

        public ClientService(AppPaths paths, IJobSender sender, ClientJobTracker tracker)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (sender == null) throw new ArgumentNullException(nameof(sender));
            if (tracker == null) throw new ArgumentNullException(nameof(tracker));
            _paths = paths;
            _sender = sender;
            Jobs = tracker;
            _sweep = new Timer(_ => SweepOldFiles(), null, SweepInterval, SweepInterval);
        }

        public ClientJobTracker Jobs { get; }

        /// <summary>The office PIN sent with every job; empty when the hosts are open.</summary>
        public string Pin { get; set; } = "";

        /// <summary>Hours a sent file is kept in sent\ for diagnostics.</summary>
        public int KeepSentFilesHours { get; set; } = AppConfig.DefaultKeepSentFilesHours;

        /// <summary>Waits before each try: three tries over 30 seconds (brief 5.2). Tests shorten them.</summary>
        public TimeSpan[] RetryDelays { get; set; } = { TimeSpan.Zero, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(20) };

        /// <summary>After a host answers "printing", its status is asked this often, up to StatusPollMax.</summary>
        public TimeSpan StatusPollInterval { get; set; } = TimeSpan.FromSeconds(10);
        public TimeSpan StatusPollMax { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>How long a file's size must stay unchanged before it is taken (tests shorten it).</summary>
        public TimeSpan StableFor { get; set; } = SpoolWatcher.DefaultStableFor;

        /// <summary>Raised on a thread-pool thread when a job reaches a final state (printed, error, pending).</summary>
        public event EventHandler<ClientJobRecord> JobFinished;

        public IList<string> WatchedPrinterIds
        {
            get { lock (_gate) { return _watched.Keys.ToList(); } }
        }

        /// <summary>Starts watching for every printer in the list and stops watching the ones that left it.</summary>
        public void SetPrinters(IEnumerable<RemotePrinter> printers)
        {
            var wanted = (printers ?? Enumerable.Empty<RemotePrinter>()).Where(p => p != null && !string.IsNullOrEmpty(p.PrinterId)).ToList();
            List<string> gone;
            lock (_gate)
            {
                gone = _watched.Keys.Where(id => wanted.All(p => !string.Equals(p.PrinterId, id, StringComparison.OrdinalIgnoreCase))).ToList();
            }
            foreach (string id in gone) StopWatching(id);
            foreach (RemotePrinter printer in wanted) StartWatching(printer);
        }

        public void StartWatching(RemotePrinter printer)
        {
            if (printer == null) throw new ArgumentNullException(nameof(printer));
            lock (_gate)
            {
                if (_disposed) return;
                Watched existing;
                if (_watched.TryGetValue(printer.PrinterId, out existing))
                {
                    existing.Printer = printer;
                    return;
                }
                string folder = ClientPrinterNames.SpoolFolder(_paths, printer.PrinterId);
                var watcher = new SpoolWatcher(folder, printer.FriendlyName) { StableFor = StableFor };
                var entry = new Watched { Printer = printer, Watcher = watcher };
                watcher.FileReady += (s, e) => OnFileReady(entry, e);
                _watched[printer.PrinterId] = entry;
                try
                {
                    watcher.Start();
                }
                catch (Exception ex)
                {
                    Log.Error("Could not start watching for jobs for \"" + printer.FriendlyName + "\" in " + folder + ".", ex);
                    _watched.Remove(printer.PrinterId);
                    watcher.Dispose();
                    throw;
                }
            }
        }

        public void StopWatching(string printerId)
        {
            Watched entry;
            lock (_gate)
            {
                if (!_watched.TryGetValue(printerId, out entry)) return;
                _watched.Remove(printerId);
            }
            entry.Watcher.Dispose();
            Log.Info("No longer watching for jobs for \"" + entry.Printer.FriendlyName + "\".");
        }

        public RemotePrinter FindPrinter(string printerId)
        {
            lock (_gate)
            {
                Watched entry;
                return _watched.TryGetValue(printerId ?? "", out entry) ? entry.Printer : null;
            }
        }

        /// <summary>Copies a file into the printer's spool folder so it is sent like a real print job.</summary>
        public void SendFile(string printerId, string sourcePath)
        {
            RemotePrinter printer = FindPrinter(printerId);
            if (printer == null) throw new InvalidOperationException("Printer " + printerId + " is not set up on this PC.");
            if (!File.Exists(sourcePath)) throw new FileNotFoundException("The file to send does not exist.", sourcePath);
            string folder = ClientPrinterNames.SpoolFolder(_paths, printerId);
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, "manual-" + DateTime.Now.ToString("HHmmss-fff") + Path.GetExtension(sourcePath));
            File.Copy(sourcePath, target, true);
            Log.Info("Copied " + sourcePath + " to " + target + " for \"" + printer.FriendlyName + "\"; the watcher will send it.");
        }

        /// <summary>Looks at the printer's folder right now instead of waiting for the next poll.</summary>
        public void ScanNow(string printerId)
        {
            Watched entry;
            lock (_gate) { _watched.TryGetValue(printerId ?? "", out entry); }
            if (entry != null) entry.Watcher.Scan("asked");
        }

        public int PendingCount(string printerId)
        {
            string folder = Path.Combine(ClientPrinterNames.SpoolFolder(_paths, printerId), ClientPrinterNames.PendingFolderName);
            return Directory.Exists(folder) ? Directory.GetFiles(folder).Length : 0;
        }

        /// <summary>Puts the files that wait in pending\ back where the watcher takes them. Returns how many.</summary>
        public int RetryPending(string printerId)
        {
            string root = ClientPrinterNames.SpoolFolder(_paths, printerId);
            string pending = Path.Combine(root, ClientPrinterNames.PendingFolderName);
            if (!Directory.Exists(pending)) return 0;
            int moved = 0;
            foreach (string file in Directory.GetFiles(pending))
            {
                string target = Path.Combine(root, "retry-" + Path.GetFileName(file).Replace("job-", ""));
                try
                {
                    File.Move(file, target);
                    moved++;
                }
                catch (Exception ex)
                {
                    Log.Warn("Could not move " + file + " back for a retry: " + ex.Message);
                }
            }
            if (moved > 0)
            {
                Log.Info("Retry: " + moved + " waiting job(s) for printer " + printerId + " put back for sending.");
                Watched entry;
                lock (_gate) { _watched.TryGetValue(printerId, out entry); }
                if (entry != null) entry.Watcher.Scan("retry");
            }
            return moved;
        }

        private void OnFileReady(Watched entry, SpoolFileReadyEventArgs file)
        {
            RemotePrinter printer = entry.Printer;
            var record = new ClientJobRecord
            {
                JobId = file.JobId,
                PrinterId = printer.PrinterId,
                PrinterFriendly = printer.FriendlyName,
                HostName = printer.HostName,
                Doc = string.IsNullOrEmpty(file.Title) ? Path.GetFileNameWithoutExtension(file.FilePath) : file.Title,
                Format = file.Format,
                Size = file.Size,
                FilePath = file.FilePath,
                State = ClientJobStates.Queued,
                Message = "Waiting to be sent to " + printer.HostName + ".",
                StartedAt = DateTime.Now
            };
            Jobs.Upsert(record);
            Task.Run(() => SendWithRetriesAsync(printer, record, _stopping.Token));
        }

        private async Task SendWithRetriesAsync(RemotePrinter printer, ClientJobRecord record, CancellationToken ct)
        {
            string jobId = record.JobId;
            string lastError = null;
            TimeSpan[] delays = RetryDelays == null || RetryDelays.Length == 0 ? new[] { TimeSpan.Zero } : RetryDelays;

            for (int attempt = 0; attempt < delays.Length; attempt++)
            {
                try
                {
                    if (delays[attempt] > TimeSpan.Zero)
                    {
                        Log.Info(jobId, "Try " + (attempt + 1) + " of " + delays.Length + " in " + delays[attempt].TotalSeconds + " s.");
                        await Task.Delay(delays[attempt], ct).ConfigureAwait(false);
                    }
                    Update(record, ClientJobStates.Sending, "Sending to " + printer.HostName + (attempt == 0 ? "." : " (try " + (attempt + 1) + ")."));

                    RequestHeader header = RequestHeader.ForJob(printer.PrinterId, Path.GetFileName(record.FilePath), record.Format,
                        record.Size, record.Doc, Pin);
                    header.JobId = jobId;
                    JobReply reply = await _sender.SendAsync(printer, header, record.FilePath, ct).ConfigureAwait(false);
                    await HandleReplyAsync(printer, record, reply, ct).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException)
                {
                    Log.Info(jobId, "Sending stopped because PrintVect is closing; the file stays in " + record.FilePath + ".");
                    return;
                }
                catch (Exception ex) when (IsReachabilityProblem(ex))
                {
                    lastError = ex.Message;
                    Log.Warn(jobId, "Try " + (attempt + 1) + " of " + delays.Length + " failed: " + ex.Message);
                }
                catch (Exception ex)
                {
                    Log.Error(jobId, "The job could not be sent.", ex);
                    Finish(record, ClientJobStates.Error, "The job could not be sent: " + ex.Message, ClientPrinterNames.FailedFolderName);
                    return;
                }
            }

            Finish(record, ClientJobStates.Pending,
                printer.HostName + " could not be reached after " + delays.Length + " tries (" + lastError + "). "
                + "The job is kept; ask for that PC to be switched on, then press Retry.", ClientPrinterNames.PendingFolderName);
        }

        private async Task HandleReplyAsync(RemotePrinter printer, ClientJobRecord record, JobReply reply, CancellationToken ct)
        {
            if (reply == null)
            {
                Finish(record, ClientJobStates.Error, printer.HostName + " gave no answer.", ClientPrinterNames.FailedFolderName);
                return;
            }
            if (!reply.Ok)
            {
                Finish(record, ClientJobStates.Error, printer.HostName + " refused the job: " + reply.Message, ClientPrinterNames.FailedFolderName);
                return;
            }
            if (reply.State == JobStates.Printed)
            {
                Finish(record, ClientJobStates.Printed, reply.Message, ClientPrinterNames.SentFolderName);
                return;
            }
            if (reply.State == JobStates.Error)
            {
                Finish(record, ClientJobStates.Error, reply.Message, ClientPrinterNames.FailedFolderName);
                return;
            }

            // The host accepted the job and is still printing: keep asking until it is done.
            MoveFile(record, ClientPrinterNames.SentFolderName);
            Update(record, ClientJobStates.Printing, reply.Message);
            DateTime until = DateTime.UtcNow + StatusPollMax;
            while (DateTime.UtcNow < until)
            {
                await Task.Delay(StatusPollInterval, ct).ConfigureAwait(false);
                JobReply status;
                try
                {
                    status = await _sender.StatusAsync(printer, record.JobId, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsReachabilityProblem(ex))
                {
                    Log.Warn(record.JobId, "Status check failed: " + ex.Message);
                    continue;
                }
                if (status == null) continue;
                if (status.State == JobStates.Printed || (status.Ok && JobStates.IsFinal(status.State) && status.State != JobStates.Error))
                {
                    Finish(record, ClientJobStates.Printed, status.Message, null);
                    return;
                }
                if (status.State == JobStates.Error || !status.Ok)
                {
                    Finish(record, ClientJobStates.Error, status.Message, null);
                    return;
                }
                Update(record, ClientJobStates.Printing, status.Message);
            }
            Finish(record, ClientJobStates.Printing, "Sent to " + printer.HostName + "; it was still printing after "
                                                      + StatusPollMax.TotalMinutes + " minutes. Check the printer there.", null);
        }

        private void Update(ClientJobRecord record, string state, string message)
        {
            record.State = state;
            record.Message = message ?? "";
            Jobs.Upsert(record);
        }

        private void Finish(ClientJobRecord record, string state, string message, string moveToFolder)
        {
            if (moveToFolder != null) MoveFile(record, moveToFolder);
            Update(record, state, message);
            Log.Info(record.JobId, "Finished: " + state + ". " + record.Message);
            EventHandler<ClientJobRecord> handler = JobFinished;
            if (handler != null)
            {
                try { handler(this, record.Clone()); }
                catch (Exception ex) { Log.Error(record.JobId, "A job listener failed.", ex); }
            }
        }

        private void MoveFile(ClientJobRecord record, string folderName)
        {
            try
            {
                if (!File.Exists(record.FilePath)) return;
                string folder = Path.Combine(Path.GetDirectoryName(record.FilePath), folderName);
                Directory.CreateDirectory(folder);
                string target = Path.Combine(folder, Path.GetFileName(record.FilePath));
                if (File.Exists(target)) File.Delete(target);
                File.Move(record.FilePath, target);
                record.FilePath = target;
                if (folderName == ClientPrinterNames.FailedFolderName) TrimFolder(folder, MaxFailedFiles);
            }
            catch (Exception ex)
            {
                Log.Warn(record.JobId, "Could not move the file to " + folderName + "\\: " + ex.Message);
            }
        }

        /// <summary>Deletes sent files older than KeepSentFilesHours and keeps failed\ at 20 files.</summary>
        public void SweepOldFiles()
        {
            List<string> ids;
            lock (_gate) { ids = _watched.Keys.ToList(); }
            foreach (string id in ids)
            {
                string root = ClientPrinterNames.SpoolFolder(_paths, id);
                try
                {
                    string sent = Path.Combine(root, ClientPrinterNames.SentFolderName);
                    if (Directory.Exists(sent))
                    {
                        DateTime cutoff = DateTime.Now.AddHours(-Math.Max(1, KeepSentFilesHours));
                        int deleted = 0;
                        foreach (string file in Directory.GetFiles(sent))
                        {
                            if (File.GetLastWriteTime(file) < cutoff)
                            {
                                File.Delete(file);
                                deleted++;
                            }
                        }
                        if (deleted > 0) Log.Info("Deleted " + deleted + " sent file(s) older than " + KeepSentFilesHours + " h from " + sent);
                    }
                    string failed = Path.Combine(root, ClientPrinterNames.FailedFolderName);
                    if (Directory.Exists(failed)) TrimFolder(failed, MaxFailedFiles);
                }
                catch (Exception ex)
                {
                    Log.Warn("Cleaning " + root + " failed: " + ex.Message);
                }
            }
        }

        private static void TrimFolder(string folder, int keep)
        {
            var files = new DirectoryInfo(folder).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).ToList();
            foreach (FileInfo old in files.Skip(keep))
            {
                try { old.Delete(); }
                catch (Exception ex) { Log.Warn("Could not delete " + old.FullName + ": " + ex.Message); }
            }
        }

        private static bool IsReachabilityProblem(Exception ex)
        {
            return ex is HostUnreachableException || ex is TimeoutException || ex is SocketException || ex is IOException;
        }

        public void Dispose()
        {
            List<Watched> entries;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                entries = _watched.Values.ToList();
                _watched.Clear();
            }
            _stopping.Cancel();
            _sweep.Dispose();
            foreach (Watched entry in entries) entry.Watcher.Dispose();
        }

        private sealed class Watched
        {
            public RemotePrinter Printer;
            public SpoolWatcher Watcher;
        }
    }
}
