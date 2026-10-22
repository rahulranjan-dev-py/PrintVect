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
        private readonly ILocalPrintQueue _localQueue;
        private readonly object _gate = new object();
        private readonly Dictionary<string, Watched> _watched = new Dictionary<string, Watched>(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        private readonly Timer _sweep;
        private bool _disposed;

        public ClientService(AppPaths paths, IJobSender sender, ClientJobTracker tracker) : this(paths, sender, tracker, new WinspoolPrintQueue())
        {
        }

        /// <param name="localQueue">Reads the virtual printer's queue for document names; tests pass a fake.</param>
        public ClientService(AppPaths paths, IJobSender sender, ClientJobTracker tracker, ILocalPrintQueue localQueue)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (sender == null) throw new ArgumentNullException(nameof(sender));
            if (tracker == null) throw new ArgumentNullException(nameof(tracker));
            _paths = paths;
            _sender = sender;
            _localQueue = localQueue;
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

        /// <summary>
        /// After this many status checks in a row without an answer, the host is taken to be switched off and the job
        /// goes back to pending\ for Retry (it may not have printed). About 30 s with the default interval.
        /// </summary>
        public int LostAfterUnansweredChecks { get; set; } = 3;

        /// <summary>How long a file's size must stay unchanged before it is taken (tests shorten it).</summary>
        public TimeSpan StableFor { get; set; } = SpoolWatcher.DefaultStableFor;

        /// <summary>
        /// Asked before a job that a program printed is sent (M6b-2, the copies prompt): gets the job with the copies its
        /// print ticket asks for, answers how many copies to print, or null to cancel. Null property: nobody is asked.
        /// Test pages and Retry are never asked.
        /// </summary>
        public Func<ClientJobRecord, Task<int?>> AskCopies { get; set; }

        /// <summary>
        /// Looks for a host that did not answer at its address (0.5.0): true when it was found somewhere else and the
        /// printer now carries the new address. A PC on a phone hotspot gets a new address each time it connects.
        /// Null: no search (tests, pvct-send).
        /// </summary>
        public Func<RemotePrinter, CancellationToken, Task<bool>> FindHost { get; set; }

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
                RecoverInterrupted(folder, printer);
                var watcher = new SpoolWatcher(folder, printer.FriendlyName, printer.LocalPrinterName, _localQueue) { StableFor = StableFor };
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

        /// <summary>The printers being watched right now (a copy, safe to use on any thread).</summary>
        public IList<RemotePrinter> WatchedPrinters
        {
            get { lock (_gate) { return _watched.Values.Select(w => w.Printer).ToList(); } }
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
            // Keep the file's own name so the host's job list and the balloons show it (e.g. "PrintVect-test-page").
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string extension = Path.GetExtension(sourcePath);
            string target = Path.Combine(folder, baseName + extension);
            for (int n = 2; File.Exists(target); n++)
            {
                target = Path.Combine(folder, baseName + "-" + n + extension);
            }
            File.Copy(sourcePath, target, false);
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

        /// <summary>
        /// Sends the files that wait in pending\ again, each under the job id it already has, so the
        /// job keeps its row in the list. Works after a restart too (the id comes from the file name).
        /// Returns how many were put back on their way.
        /// </summary>
        public int RetryPending(string printerId)
        {
            RemotePrinter printer = FindPrinter(printerId);
            if (printer == null)
            {
                Log.Warn("Retry: printer " + printerId + " is not watched; nothing sent.");
                return 0;
            }
            return PendingFiles(printerId).Count(file => Resend(printer, file));
        }

        /// <summary>Sends one waiting job again (right-click, Retry on the Jobs page). False when it is not waiting.</summary>
        public bool RetryJob(string jobId)
        {
            ClientJobRecord record = Jobs.Find(jobId);
            RemotePrinter printer = record == null ? null : FindPrinter(record.PrinterId);
            string file = record == null ? null : PendingFileOf(record);
            if (printer == null || file == null)
            {
                Log.Warn(jobId, "Retry: this job is not waiting (or its printer is no longer on this PC); nothing sent.");
                return false;
            }
            return Resend(printer, file);
        }

        /// <summary>
        /// Cancels a waiting job (right-click, Cancel on the Jobs page): its file is deleted and it is never sent.
        /// False when the job is not waiting (it is being sent, printing, or already done).
        /// </summary>
        public bool CancelWaiting(string jobId)
        {
            ClientJobRecord record = Jobs.Find(jobId);
            if (record == null || record.State != ClientJobStates.Pending)
            {
                return false;
            }
            string file = PendingFileOf(record);
            if (file != null)
            {
                record.FilePath = file;
            }
            Log.Info(jobId, "Waiting job cancelled by the user on this PC.");
            Cancel(record);
            return true;
        }

        /// <summary>Cancels every waiting job of every printer on this PC, files without a list entry included.</summary>
        public int CancelAllWaiting()
        {
            int cancelled = 0;
            foreach (RemotePrinter printer in WatchedPrinters)
            {
                foreach (string file in PendingFiles(printer.PrinterId))
                {
                    string jobId = JobIdFromFileName(file);
                    if (jobId != null && CancelWaiting(jobId))
                    {
                        cancelled++;
                        continue;
                    }
                    try
                    {
                        File.Delete(file);
                        cancelled++;
                        Log.Info(jobId, "Waiting file " + file + " deleted (Cancel all waiting).");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(jobId, "Waiting file " + file + " could not be deleted: " + ex.Message);
                    }
                }
            }
            foreach (ClientJobRecord record in Jobs.All().Where(r => r.State == ClientJobStates.Pending))
            {
                if (CancelWaiting(record.JobId)) cancelled++;   // waiting rows whose printer was removed from this PC
            }
            return cancelled;
        }

        private IList<string> PendingFiles(string printerId)
        {
            string pending = Path.Combine(ClientPrinterNames.SpoolFolder(_paths, printerId), ClientPrinterNames.PendingFolderName);
            try
            {
                return Directory.Exists(pending) ? Directory.GetFiles(pending) : new string[0];
            }
            catch (Exception ex)
            {
                Log.Warn("The waiting jobs in " + pending + " could not be listed: " + ex.Message);
                return new string[0];
            }
        }

        /// <summary>The job's file in pending\ (where Finish put it, or found by its id after a restart), or null.</summary>
        private string PendingFileOf(ClientJobRecord record)
        {
            if (!string.IsNullOrEmpty(record.FilePath) && File.Exists(record.FilePath)
                && string.Equals(Path.GetFileName(Path.GetDirectoryName(record.FilePath)), ClientPrinterNames.PendingFolderName, StringComparison.OrdinalIgnoreCase))
            {
                return record.FilePath;
            }
            return PendingFiles(record.PrinterId).FirstOrDefault(f => string.Equals(JobIdFromFileName(f), record.JobId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Moves one file from pending\ back to the printer's folder and sends it under the job id it already has.</summary>
        private bool Resend(RemotePrinter printer, string file)
        {
            string root = ClientPrinterNames.SpoolFolder(_paths, printer.PrinterId);
            string jobId = JobIdFromFileName(file) ?? Guid.NewGuid().ToString("D");
            string target = Path.Combine(root, SpoolWatcher.JobPrefix + jobId + Path.GetExtension(file));
            try
            {
                if (File.Exists(target)) File.Delete(target);
                File.Move(file, target);
            }
            catch (Exception ex)
            {
                Log.Warn(jobId, "Could not move " + file + " back for a retry: " + ex.Message);
                return false;
            }

            ClientJobRecord record = Jobs.Find(jobId);
            if (record == null)
            {
                XpsFileInfo info = XpsFormatSniffer.Inspect(target);
                record = new ClientJobRecord
                {
                    JobId = jobId,
                    PrinterId = printer.PrinterId,
                    PrinterFriendly = printer.FriendlyName,
                    HostName = printer.HostName,
                    Doc = DocumentName(info.Title, target),
                    Copies = Math.Max(1, info.Copies),
                    Format = info.Format ?? JobFormats.FromFileName(target),
                    Size = new FileInfo(target).Length,
                    StartedAt = DateTime.Now
                };
            }
            record.FilePath = target;
            Update(record, ClientJobStates.Queued, "Waiting to be sent again to " + printer.HostName + ".");
            Log.Info(jobId, "Retry: sending the waiting job to " + printer.HostName + " again.");
            ClientJobRecord toSend = record;
            Task.Run(() => SendWithRetriesAsync(printer, toSend, _stopping.Token));
            return true;
        }

        /// <summary>
        /// A job-* file still in the folder root was being sent when PrintVect closed: move it to
        /// pending\ so Retry can send it, and mark its record.
        /// </summary>
        private void RecoverInterrupted(string folder, RemotePrinter printer)
        {
            if (!Directory.Exists(folder)) return;
            foreach (string file in Directory.GetFiles(folder, SpoolWatcher.JobPrefix + "*"))
            {
                string jobId = JobIdFromFileName(file) ?? Guid.NewGuid().ToString("D");
                string pending = Path.Combine(folder, ClientPrinterNames.PendingFolderName, Path.GetFileName(file));
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(pending));
                    if (File.Exists(pending)) File.Delete(pending);
                    File.Move(file, pending);
                }
                catch (Exception ex)
                {
                    Log.Warn(jobId, "Interrupted job file " + file + " could not be moved to pending: " + ex.Message);
                    continue;
                }
                ClientJobRecord record = Jobs.Find(jobId);
                if (record == null)
                {
                    XpsFileInfo info = XpsFormatSniffer.Inspect(pending);
                    record = new ClientJobRecord
                    {
                        JobId = jobId, PrinterId = printer.PrinterId, PrinterFriendly = printer.FriendlyName, HostName = printer.HostName,
                        Doc = DocumentName(info.Title, pending), Copies = Math.Max(1, info.Copies), Format = info.Format ?? JobFormats.FromFileName(pending),
                        Size = new FileInfo(pending).Length, StartedAt = File.GetLastWriteTime(pending)
                    };
                }
                record.FilePath = pending;
                Update(record, ClientJobStates.Pending, "PrintVect was closed before this job was sent. Press Retry waiting.");
                Log.Info(jobId, "Interrupted job moved to pending\\ for Retry.");
            }
        }

        /// <summary>"job-{guid}.xps" to the guid, else null.</summary>
        public static string JobIdFromFileName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path) ?? "";
            if (!name.StartsWith(SpoolWatcher.JobPrefix, StringComparison.OrdinalIgnoreCase)) return null;
            Guid id;
            return Guid.TryParse(name.Substring(SpoolWatcher.JobPrefix.Length), out id) ? id.ToString("D") : null;
        }

        private static string DocumentName(string document, string filePath)
        {
            if (!string.IsNullOrWhiteSpace(document)) return document.Trim();
            string name = Path.GetFileNameWithoutExtension(filePath) ?? "document";
            return name.StartsWith(SpoolWatcher.JobPrefix, StringComparison.OrdinalIgnoreCase) ? "Print job" : name;
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
                Doc = DocumentName(file.Document, file.FilePath),
                Format = file.Format,
                Size = file.Size,
                Copies = Math.Max(1, file.Copies),
                FilePath = file.FilePath,
                State = ClientJobStates.Queued,
                Message = "Waiting to be sent to " + printer.HostName + ".",
                StartedAt = DateTime.Now
            };
            Jobs.Upsert(record);
            bool ask = file.FromPrinter;
            Task.Run(() => AskThenSendAsync(printer, record, ask, _stopping.Token));
        }

        private async Task AskThenSendAsync(RemotePrinter printer, ClientJobRecord record, bool fromPrinter, CancellationToken ct)
        {
            Func<ClientJobRecord, Task<int?>> ask = AskCopies;
            if (fromPrinter && ask != null)
            {
                Update(record, ClientJobStates.Queued, "Waiting for the number of copies on this PC.");
                int? copies;
                try
                {
                    copies = await ask(record.Clone()).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Error(record.JobId, "The copies question failed; sending " + record.Copies + " cop(ies) as the document asked.", ex);
                    copies = record.Copies;
                }
                if (ct.IsCancellationRequested)
                {
                    return;   // PrintVect is closing; the file stays and is offered for Retry at the next start
                }
                if (copies == null)
                {
                    Cancel(record);
                    return;
                }
                record.Copies = Math.Max(1, Math.Min(copies.Value, XpsFormatSniffer.MaxCopies));
                Log.Info(record.JobId, "Copies chosen on this PC: " + record.Copies + ".");
            }
            await SendWithRetriesAsync(printer, record, ct).ConfigureAwait(false);
        }

        private void Cancel(ClientJobRecord record)
        {
            try
            {
                if (File.Exists(record.FilePath)) File.Delete(record.FilePath);
            }
            catch (Exception ex)
            {
                Log.Warn(record.JobId, "The cancelled job's file could not be deleted: " + ex.Message);
            }
            Finish(record, ClientJobStates.Cancelled, "Cancelled on this PC; nothing was sent.", null);
        }

        private async Task SendWithRetriesAsync(RemotePrinter printer, ClientJobRecord record, CancellationToken ct)
        {
            string jobId = record.JobId;
            string lastError = null;
            TimeSpan[] delays = RetryDelays == null || RetryDelays.Length == 0 ? new[] { TimeSpan.Zero } : RetryDelays;
            bool searched = false;
            bool foundAgain = false;

            for (int attempt = 0; attempt < delays.Length; attempt++)
            {
                try
                {
                    if (foundAgain)
                    {
                        foundAgain = false;   // the host answered somewhere else: try there at once
                    }
                    else if (delays[attempt] > TimeSpan.Zero)
                    {
                        Log.Info(jobId, "Try " + (attempt + 1) + " of " + delays.Length + " in " + delays[attempt].TotalSeconds + " s.");
                        await Task.Delay(delays[attempt], ct).ConfigureAwait(false);
                    }
                    Update(record, ClientJobStates.Sending, "Sending to " + printer.HostName + (attempt == 0 ? "." : " (try " + (attempt + 1) + ")."));

                    RequestHeader header = RequestHeader.ForJob(printer.PrinterId, Path.GetFileName(record.FilePath), record.Format,
                        record.Size, record.Doc, Pin);
                    header.JobId = jobId;
                    header.Copies = Math.Max(1, record.Copies);
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

                if (!searched && FindHost != null && attempt + 1 < delays.Length)
                {
                    searched = true;
                    try
                    {
                        foundAgain = await SearchForHostAsync(printer, record, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        Log.Info(jobId, "Sending stopped because PrintVect is closing; the file stays in " + record.FilePath + ".");
                        return;
                    }
                }
            }

            Finish(record, ClientJobStates.Pending,
                printer.HostName + " did not answer (" + delays.Length + " tries). " + lastError + " The document is kept on this PC: "
                + "when " + printer.HostName + " is back, press Retry waiting.", ClientPrinterNames.PendingFolderName);
        }

        /// <summary>One search of the office for a host that did not answer; false when it was not found elsewhere.</summary>
        private async Task<bool> SearchForHostAsync(RemotePrinter printer, ClientJobRecord record, CancellationToken ct)
        {
            Update(record, ClientJobStates.Sending, "Looking for " + printer.HostName + " on the network.");
            string before = printer.HostIp;
            bool found;
            try
            {
                found = await FindHost(printer, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Log.Warn(record.JobId, "The search for " + printer.HostName + " failed: " + ex.Message);
                return false;
            }
            if (found)
            {
                Log.Info(record.JobId, printer.HostName + " now answers at " + printer.HostIp + " (was " + before + "); sending there.");
            }
            else
            {
                Log.Info(record.JobId, printer.HostName + " was not found at another address.");
            }
            return found;
        }

        private async Task HandleReplyAsync(RemotePrinter printer, ClientJobRecord record, JobReply reply, CancellationToken ct)
        {
            if (reply == null)
            {
                Finish(record, ClientJobStates.Error, printer.HostName + " gave no answer.", ClientPrinterNames.FailedFolderName);
                return;
            }
            if (!reply.Ok && reply.Retry)
            {
                FinishLost(record, reply.Message + " Press Retry waiting to send it again.");
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
            string lastNews = reply.Message;
            int unanswered = 0;
            while (DateTime.UtcNow < until)
            {
                await Task.Delay(StatusPollInterval, ct).ConfigureAwait(false);
                JobReply status;
                try
                {
                    status = await _sender.StatusAsync(printer, record.JobId, ct).ConfigureAwait(false);
                    unanswered = 0;
                }
                catch (Exception ex) when (IsReachabilityProblem(ex))
                {
                    unanswered++;
                    Log.Warn(record.JobId, "Status check " + unanswered + " without an answer: " + ex.Message);
                    if (unanswered >= LostAfterUnansweredChecks)
                    {
                        FinishLost(record, printer.HostName + " stopped answering after it received this job (switched off or disconnected?), "
                                           + "so it may not have printed. If the page did not come out, press Retry waiting when "
                                           + printer.HostName + " is back.");
                        return;
                    }
                    continue;
                }
                if (status == null) continue;
                if (!status.Ok && status.Retry)
                {
                    FinishLost(record, status.Message + " Press Retry waiting to send it again.");
                    return;
                }
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
                lastNews = status.Message;
                Update(record, ClientJobStates.Printing, status.Message);
            }
            Finish(record, ClientJobStates.Printing, "Sent to " + printer.HostName + ". After " + StatusPollMax.TotalMinutes
                                                      + " minutes it still says: " + lastNews + " Its Jobs page shows how it ends.", null);
        }

        /// <summary>
        /// The job did not print but sending it again makes sense (the host lost it, was switched off, or wants the office
        /// PIN): put the file back in pending\ so Retry waiting sends it again under the same job id.
        /// </summary>
        private void FinishLost(ClientJobRecord record, string message)
        {
            Log.Warn(record.JobId, "Kept for Retry: " + message);
            Finish(record, ClientJobStates.Pending, message, ClientPrinterNames.PendingFolderName);
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
                string folder = Path.Combine(ClientPrinterNames.SpoolFolder(_paths, record.PrinterId), folderName);
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
