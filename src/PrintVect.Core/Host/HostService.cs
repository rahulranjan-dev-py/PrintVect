using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core.Config;
using PrintVect.Core.Logging;
using PrintVect.Core.Printing;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Host
{
    /// <summary>
    /// The host role (brief 5.3): listens on the job port on every IPv4 address, receives one job
    /// per connection into spool\incoming\, hands it to the print worker, and answers one JSON
    /// line. Also answers "status" and "list" requests. Every step is logged with the jobId.
    /// </summary>
    public sealed class HostService : IDisposable
    {
        private readonly IPrintEngine _engine;
        private readonly IPrinterStatusSource _printers;
        private readonly JobTracker _tracker;
        private readonly IncomingSpool _spool;
        private readonly ConcurrentDictionary<TcpClient, byte> _connections = new ConcurrentDictionary<TcpClient, byte>();
        private readonly object _lifecycle = new object();
        private SharedPrinter[] _shared = new SharedPrinter[0];
        private string _pin = "";
        private TcpListener _listener;
        private CancellationTokenSource _stopping;
        private PrintDispatcher _dispatcher;

        public HostService(AppPaths paths, IPrintEngine engine, IPrinterStatusSource printers, JobTracker tracker)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            if (printers == null) throw new ArgumentNullException(nameof(printers));
            if (tracker == null) throw new ArgumentNullException(nameof(tracker));
            _engine = engine;
            _printers = printers;
            _tracker = tracker;
            _spool = new IncomingSpool(paths);
        }

        public JobTracker Jobs
        {
            get { return _tracker; }
        }

        /// <summary>The shared PIN; empty means open to the office network.</summary>
        public string Pin
        {
            get { return Volatile.Read(ref _pin); }
            set { Volatile.Write(ref _pin, value ?? ""); }
        }

        /// <summary>The port actually bound (useful when 0 was requested).</summary>
        public int Port { get; private set; }

        public DateTime? StartedAt { get; private set; }

        public bool IsListening
        {
            get { lock (_lifecycle) { return _listener != null; } }
        }

        public string IncomingDirectory
        {
            get { return _spool.Directory; }
        }

        public int PendingPrintCount
        {
            get { PrintDispatcher dispatcher = _dispatcher; return dispatcher == null ? 0 : dispatcher.PendingCountAll; }
        }

        /// <summary>How long a job connection waits for "printed" before answering with the current state.</summary>
        public TimeSpan ReplyWait { get; set; } = ProtocolConstants.ReplyWait;

        /// <summary>
        /// After this long inside the print engine, a job is reported as stuck and later jobs for that
        /// printer get a fresh print thread. Longer than XpsPrintEngine.CompletionWait, so it only fires
        /// when a Windows call itself never returns (seen with System.Printing's AddJob).
        /// </summary>
        public TimeSpan StuckTimeout { get; set; } = TimeSpan.FromMinutes(20);

        public IList<SharedPrinter> SharedPrinters
        {
            get { return Volatile.Read(ref _shared).ToList(); }
        }

        /// <summary>Raised (on a background thread) once the file has arrived and is queued for the printer.</summary>
        public event EventHandler<JobRecord> JobReceived;

        /// <summary>Raised (on a background thread) when the job is printed, failed, or still printing after the wait.</summary>
        public event EventHandler<JobRecord> JobFinished;

        public void UpdateSharedPrinters(IEnumerable<SharedPrinter> printers)
        {
            SharedPrinter[] snapshot = (printers ?? Enumerable.Empty<SharedPrinter>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.LocalName))
                .Select(p => new SharedPrinter
                {
                    Id = string.IsNullOrWhiteSpace(p.Id) ? PrinterIds.For(Environment.MachineName, p.LocalName) : p.Id,
                    LocalName = p.LocalName,
                    FriendlyName = string.IsNullOrWhiteSpace(p.FriendlyName) ? p.LocalName : p.FriendlyName.Trim()
                })
                .ToArray();
            Volatile.Write(ref _shared, snapshot);
            Log.Info("Shared printers: " + (snapshot.Length == 0
                ? "(none)"
                : string.Join(", ", snapshot.Select(p => "\"" + p.FriendlyName + "\" [" + p.LocalName + ", id " + p.Id + "]"))));
        }

        /// <summary>Starts listening. Throws SocketException when the port is in use.</summary>
        public void Start(int port)
        {
            lock (_lifecycle)
            {
                if (_listener != null)
                {
                    return;
                }

                _spool.Ensure();
                int swept = _spool.SweepOlderThan(TimeSpan.FromHours(24));

                var listener = new TcpListener(IPAddress.Any, port);
                listener.Start(16);
                _listener = listener;
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                _stopping = new CancellationTokenSource();
                _dispatcher = new PrintDispatcher(_engine);
                StartedAt = DateTime.Now;

                CancellationToken token = _stopping.Token;
                Task acceptLoop = Task.Run(() => AcceptLoopAsync(listener, token));
                Task watched = acceptLoop.ContinueWith(t => Log.Error("The accept loop ended unexpectedly.", t.Exception == null ? null : t.Exception.GetBaseException()),
                    TaskContinuationOptions.OnlyOnFaulted);
                Log.Info("Sharing ON: listening for print jobs on TCP port " + Port + " on every network card. Received files go to "
                         + _spool.Directory + (swept > 0 ? " (" + swept + " old file(s) removed)." : "."));
            }
        }

        public void Stop()
        {
            TcpListener listener;
            CancellationTokenSource stopping;
            PrintDispatcher dispatcher;
            lock (_lifecycle)
            {
                if (_listener == null)
                {
                    return;
                }
                listener = _listener;
                stopping = _stopping;
                dispatcher = _dispatcher;
                _listener = null;
                _stopping = null;
                _dispatcher = null;
                StartedAt = null;
            }

            Log.Info("Sharing OFF: closing the job listener on port " + Port + ".");
            stopping.Cancel();
            try
            {
                listener.Stop();
            }
            catch (Exception ex)
            {
                Log.Warn("Stopping the listener failed: " + ex.Message);
            }
            foreach (TcpClient client in _connections.Keys)
            {
                SafeClose(client);
            }
            dispatcher.Stop();
        }

        public void Dispose()
        {
            Stop();
        }

        private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested)
                    {
                        break;
                    }
                    Log.Error("Accepting a connection failed; the listener keeps running.", ex);
                    try { await Task.Delay(500, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
                    continue;
                }

                _connections.TryAdd(client, 0);
                TcpClient accepted = client;
                Task tracked = Task.Run(() => HandleConnectionAsync(accepted, ct)).ContinueWith(t =>
                {
                    byte unused;
                    _connections.TryRemove(accepted, out unused);
                    if (t.IsFaulted)
                    {
                        Log.Error("A connection handler failed unexpectedly.", t.Exception == null ? null : t.Exception.GetBaseException());
                    }
                }, TaskScheduler.Default);
            }
            Log.Info("Job listener stopped.");
        }

        private async Task HandleConnectionAsync(TcpClient client, CancellationToken ct)
        {
            string remote = Describe(client);
            string localIp = LocalAddress(client);
            string jobId = null;
            var watch = Stopwatch.StartNew();

            using (client)
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
            using (timeout.Token.Register(() => SafeClose(client)))
            {
                timeout.CancelAfter(ProtocolConstants.TransferTimeout);
                NetworkStream stream = null;
                try
                {
                    client.NoDelay = true;
                    stream = client.GetStream();
                    Log.Info("Connection from " + remote + ".");

                    RequestHeader header = await Framing.ReadHeaderAsync(stream, timeout.Token).ConfigureAwait(false);
                    jobId = header.JobId;

                    object reply;
                    if (header.Version != ProtocolConstants.Version)
                    {
                        Log.Warn(jobId, "Request from " + remote + " uses protocol version " + header.Version
                                        + "; this PrintVect speaks version " + ProtocolConstants.Version + ".");
                        await DrainBodyAsync(stream, header, jobId, timeout.Token).ConfigureAwait(false);
                        reply = JobReply.Error(jobId, "This PC runs PrintVect protocol version " + ProtocolConstants.Version
                                                      + " but the sender uses version " + header.Version
                                                      + ". Install the same PrintVect version on both PCs.");
                    }
                    else
                    {
                        switch (header.Type)
                        {
                            case RequestTypes.List:
                                reply = BuildListReply(localIp);
                                break;
                            case RequestTypes.Status:
                                reply = HandleStatus(header);
                                break;
                            case RequestTypes.Job:
                                reply = await HandleJobAsync(header, stream, remote, timeout.Token).ConfigureAwait(false);
                                break;
                            default:
                                Log.Warn(jobId, "Unknown request type \"" + header.Type + "\" from " + remote + ".");
                                reply = JobReply.Error(jobId, "Unknown request type \"" + header.Type + "\".");
                                break;
                        }
                    }

                    await Framing.WriteReplyAsync(stream, reply, timeout.Token).ConfigureAwait(false);
                    Log.Info(jobId, "Replied to " + remote + " after " + watch.ElapsedMilliseconds + " ms: " + Framing.EncodeReply(reply));
                }
                catch (ProtocolException ex)
                {
                    Log.Warn(jobId, "Bad request from " + remote + ": " + ex.Message);
                    await TryReplyAsync(stream, JobReply.Error(jobId, ex.Message)).ConfigureAwait(false);
                }
                catch (EndOfStreamException ex)
                {
                    Log.Warn(jobId, "Connection from " + remote + " ended early: " + ex.Message);
                }
                catch (Exception ex) when (ct.IsCancellationRequested)
                {
                    Log.Info(jobId, "Connection from " + remote + " closed because sharing was turned off (" + ex.GetType().Name + ").");
                }
                catch (Exception ex) when (timeout.IsCancellationRequested)
                {
                    Log.Warn(jobId, "Connection from " + remote + " timed out after " + ProtocolConstants.TransferTimeout.TotalMinutes
                                    + " minutes (" + ex.GetType().Name + ").");
                }
                catch (Exception ex)
                {
                    Log.Error(jobId, "Connection from " + remote + " failed.", ex);
                    await TryReplyAsync(stream, JobReply.Error(jobId, "The host " + Environment.MachineName
                                                                      + " hit an unexpected problem: " + ex.Message)).ConfigureAwait(false);
                }
            }
        }

        private ListReply BuildListReply(string localIp)
        {
            SharedPrinter[] shared = Volatile.Read(ref _shared);
            IList<LocalPrinterInfo> statuses;
            try
            {
                statuses = _printers.GetPrinters();
            }
            catch (Exception ex)
            {
                Log.Warn("Printer statuses could not be read for the list reply: " + ex.Message);
                statuses = new List<LocalPrinterInfo>();
            }

            var reply = new ListReply { Host = Environment.MachineName, Ip = localIp, Port = Port };
            foreach (SharedPrinter printer in shared)
            {
                LocalPrinterInfo local = statuses.FirstOrDefault(s => string.Equals(s.Name, printer.LocalName, StringComparison.OrdinalIgnoreCase));
                reply.Printers.Add(new PrinterInfo
                {
                    Id = printer.Id,
                    Name = printer.LocalName,
                    Friendly = printer.FriendlyName,
                    Status = local == null ? PrinterStatuses.Unknown : local.Status
                });
            }
            return reply;
        }

        private JobReply HandleStatus(RequestHeader header)
        {
            if (string.IsNullOrWhiteSpace(header.JobId))
            {
                return JobReply.Error(null, "A status request needs a jobId.");
            }

            JobRecord record;
            if (!_tracker.TryGet(header.JobId, out record))
            {
                return JobReply.Error(header.JobId, Environment.MachineName + " has no record of job " + header.JobId
                                                    + ". It may have arrived before PrintVect was last restarted.");
            }
            return new JobReply { Ok = record.State != JobStates.Error, JobId = record.JobId, State = record.State, Message = record.Message };
        }

        private async Task<JobReply> HandleJobAsync(RequestHeader header, NetworkStream stream, string remote, CancellationToken ct)
        {
            Guid guid;
            if (!Guid.TryParse(header.JobId ?? "", out guid))
            {
                Log.Warn("Job from " + remote + " refused: the job id \"" + header.JobId + "\" is not a GUID.");
                await DrainBodyAsync(stream, header, null, ct).ConfigureAwait(false);
                return JobReply.Error(header.JobId, "The job id must be a GUID.");
            }
            string jobId = guid.ToString("D");

            Log.Info(jobId, string.Format(CultureInfo.InvariantCulture,
                "Job from {0}: PC {1}, user {2}, printer \"{3}\", document \"{4}\", format {5}, {6:N0} bytes.",
                remote, header.Client, header.User, header.PrinterId, header.Doc, header.Format, header.Size));

            SharedPrinter printer;
            string refusal = Validate(header, out printer);
            if (refusal != null)
            {
                Log.Warn(jobId, "Refused: " + refusal);
                await DrainBodyAsync(stream, header, jobId, ct).ConfigureAwait(false);
                return JobReply.Error(jobId, refusal);
            }

            string path = _spool.PathFor(jobId, header.Format);
            JobRecord record = _tracker.Add(new JobRecord
            {
                JobId = jobId,
                PrinterId = printer.Id,
                PrinterName = printer.LocalName,
                PrinterFriendly = printer.FriendlyName,
                Client = string.IsNullOrWhiteSpace(header.Client) ? remote : header.Client.Trim(),
                User = header.User ?? "",
                Doc = string.IsNullOrWhiteSpace(header.Doc) ? (string.IsNullOrWhiteSpace(header.FileName) ? "document" : header.FileName) : header.Doc.Trim(),
                Format = header.Format,
                Size = header.Size,
                State = JobStates.Queued,
                Message = "Receiving the file.",
                ReceivedAt = DateTime.Now,
                FilePath = path
            });

            var watch = Stopwatch.StartNew();
            try
            {
                using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, ProtocolConstants.CopyBufferSize, true))
                {
                    await Framing.CopyExactlyAsync(stream, file, header.Size, null, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _spool.DeleteQuietly(path, jobId);
                string message = ex is EndOfStreamException
                    ? "The file arrived incomplete: " + ex.Message
                    : "The file could not be saved on " + Environment.MachineName + ": " + ex.Message;
                Log.Warn(jobId, message);
                RaiseFinished(_tracker.Update(jobId, JobStates.Error, message));
                if (ex is EndOfStreamException)
                {
                    throw;
                }
                return JobReply.Error(jobId, message);
            }

            long actual = new FileInfo(path).Length;
            if (actual != header.Size)
            {
                string message = "The file arrived with " + actual + " bytes but " + header.Size + " were announced.";
                Log.Warn(jobId, message);
                _spool.DeleteQuietly(path, jobId);
                RaiseFinished(_tracker.Update(jobId, JobStates.Error, message));
                return JobReply.Error(jobId, message);
            }
            if (!LooksLikeXps(path))
            {
                string message = "The file is not an XPS document (it does not start with a ZIP signature). It was kept at "
                                 + path + " on " + Environment.MachineName + " for diagnosis.";
                Log.Warn(jobId, message);
                RaiseFinished(_tracker.Update(jobId, JobStates.Error, message));
                return JobReply.Error(jobId, message);
            }

            Log.Info(jobId, string.Format(CultureInfo.InvariantCulture, "File received: {0} ({1:N0} bytes in {2} ms).", path, actual, watch.ElapsedMilliseconds));

            PrintDispatcher dispatcher = _dispatcher;
            CancellationTokenSource stopping = _stopping;
            if (dispatcher == null || stopping == null)
            {
                string message = "Sharing was turned off on " + Environment.MachineName + " before the job could print.";
                RaiseFinished(_tracker.Update(jobId, JobStates.Error, message));
                return JobReply.Error(jobId, message);
            }

            int ahead = dispatcher.PendingCount(printer.LocalName);
            RaiseReceived(_tracker.Update(jobId, JobStates.Queued,
                ahead == 0 ? "Received on " + Environment.MachineName + "; sending to the printer." : "Received on " + Environment.MachineName + "; " + ahead + " job(s) ahead of it."));

            var request = new PrintRequest
            {
                JobId = jobId,
                PrinterName = printer.LocalName,
                FriendlyName = printer.FriendlyName,
                FilePath = path,
                Format = header.Format,
                DocumentName = record.Doc,
                ClientName = record.Client,
                UserName = record.User
            };

            Task<PrintOutcome> printing = dispatcher.EnqueueAsync(request, (state, message) => _tracker.Update(jobId, state, message));
            Task finished = printing.ContinueWith(t => OnPrintFinished(jobId, path, t), TaskScheduler.Default);
            Task watchdog = WatchForStuckJobAsync(jobId, printer, finished, dispatcher, stopping.Token);

            Task first = await Task.WhenAny(finished, Task.Delay(ReplyWait, ct)).ConfigureAwait(false);
            if (first != finished)
            {
                Log.Info(jobId, "Still not finished after " + ReplyWait.TotalSeconds + " s; answering with the current state.");
            }

            JobRecord current;
            _tracker.TryGet(jobId, out current);
            return new JobReply
            {
                Ok = current != null && current.State != JobStates.Error,
                JobId = jobId,
                State = current == null ? JobStates.Error : current.State,
                Message = current == null ? "The job record was lost." : current.Message
            };
        }

        private string Validate(RequestHeader header, out SharedPrinter printer)
        {
            printer = null;
            string host = Environment.MachineName;

            if (!PinHash.Matches(Pin, header.Pin))
            {
                return host + " requires a PIN. Enter the PIN from " + host + "'s PrintVect Settings and try again.";
            }
            if (!JobFormats.IsKnown(header.Format))
            {
                return "Format \"" + header.Format + "\" is not supported; send an .xps or .oxps file.";
            }
            if (header.Size <= 0 || header.Size > ProtocolConstants.MaxFileBytes)
            {
                return "The file size (" + header.Size + " bytes) must be between 1 byte and 200 MB.";
            }

            SharedPrinter[] shared = Volatile.Read(ref _shared);
            if (shared.Length == 0)
            {
                return host + " is not sharing any printer right now. Tick a printer under \"Share my printers\" on that PC.";
            }
            printer = SharedPrinterResolver.Find(shared, header.PrinterId);
            if (printer == null)
            {
                return "No shared printer called \"" + header.PrinterId + "\" on " + host + ". Shared printers: "
                       + string.Join(", ", shared.Select(p => "\"" + p.FriendlyName + "\"")) + ".";
            }
            return null;
        }

        /// <summary>
        /// Windows' AddJob does not return for a printer that never takes the job. After StuckTimeout the
        /// job is reported as stuck, and the printer's stuck print thread is retired so later jobs get a
        /// fresh one. If Windows finishes the job later after all, OnPrintFinished corrects the record.
        /// </summary>
        private async Task WatchForStuckJobAsync(string jobId, SharedPrinter printer, Task finished, PrintDispatcher dispatcher, CancellationToken stopping)
        {
            try
            {
                Task first = await Task.WhenAny(finished, Task.Delay(StuckTimeout, stopping)).ConfigureAwait(false);
                if (first == finished || stopping.IsCancellationRequested)
                {
                    return;
                }

                string host = Environment.MachineName;
                string message = "Windows on " + host + " has not finished sending this job to " + printer.FriendlyName + " after "
                                 + StuckTimeout.TotalMinutes.ToString(CultureInfo.InvariantCulture) + " minutes. Check the printer and open its "
                                 + "Windows print queue on " + host + ". Later jobs for this printer use a new print thread.";
                Log.Warn(jobId, message);
                RaiseFinished(_tracker.Update(jobId, JobStates.Error, message));
                dispatcher.RetireStuckWorker(printer.LocalName, jobId);
            }
            catch (Exception ex)
            {
                Log.Error(jobId, "The stuck-job watchdog failed.", ex);
            }
        }

        private void OnPrintFinished(string jobId, string path, Task<PrintOutcome> task)
        {
            try
            {
                PrintOutcome outcome = task.Status == TaskStatus.RanToCompletion
                    ? task.Result
                    : PrintOutcome.Error("Printing failed unexpectedly: " + (task.Exception == null ? "unknown reason" : task.Exception.GetBaseException().Message));

                JobRecord snapshot = _tracker.Update(jobId, outcome.State, outcome.Message);
                if (outcome.State == JobStates.Printed)
                {
                    _spool.DeleteQuietly(path, jobId);
                }
                else
                {
                    Log.Info(jobId, "File kept for diagnosis: " + path);
                }
                Log.Info(jobId, "Finished: " + outcome.State + ". " + outcome.Message);
                RaiseFinished(snapshot);
            }
            catch (Exception ex)
            {
                Log.Error(jobId, "Finishing the job failed.", ex);
            }
        }

        private static async Task DrainBodyAsync(NetworkStream stream, RequestHeader header, string jobId, CancellationToken ct)
        {
            if (header.Type != RequestTypes.Job || header.Size <= 0 || header.Size > ProtocolConstants.MaxFileBytes)
            {
                return;
            }
            try
            {
                long drained = await Framing.DrainAsync(stream, header.Size, ct).ConfigureAwait(false);
                Log.Info(jobId, "Discarded " + drained + " bytes of the refused job so the sender receives the reason.");
            }
            catch (Exception ex)
            {
                Log.Warn(jobId, "Could not read the rest of the refused job: " + ex.Message);
            }
        }

        private static async Task TryReplyAsync(NetworkStream stream, JobReply reply)
        {
            if (stream == null)
            {
                return;
            }
            try
            {
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                {
                    await Framing.WriteReplyAsync(stream, reply, cts.Token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Log.Warn(reply.JobId, "The error reply could not be delivered: " + ex.Message);
            }
        }

        private static bool LooksLikeXps(string path)
        {
            try
            {
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    return file.Length >= 4 && file.ReadByte() == 'P' && file.ReadByte() == 'K';
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not inspect " + path + ": " + ex.Message);
                return false;
            }
        }

        private void RaiseReceived(JobRecord record)
        {
            EventHandler<JobRecord> handler = JobReceived;
            if (handler == null || record == null) return;
            try { handler(this, record); }
            catch (Exception ex) { Log.Error(record.JobId, "A job-received listener failed.", ex); }
        }

        private void RaiseFinished(JobRecord record)
        {
            EventHandler<JobRecord> handler = JobFinished;
            if (handler == null || record == null) return;
            try { handler(this, record); }
            catch (Exception ex) { Log.Error(record.JobId, "A job-finished listener failed.", ex); }
        }

        private static string Describe(TcpClient client)
        {
            try
            {
                return client.Client.RemoteEndPoint == null ? "unknown" : client.Client.RemoteEndPoint.ToString();
            }
            catch (Exception)
            {
                return "unknown";
            }
        }

        private static string LocalAddress(TcpClient client)
        {
            try
            {
                var endpoint = client.Client.LocalEndPoint as IPEndPoint;
                return endpoint == null ? "" : endpoint.Address.ToString();
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static void SafeClose(TcpClient client)
        {
            try
            {
                client.Close();
            }
            catch (Exception ex)
            {
                Log.Warn("Closing a connection failed: " + ex.Message);
            }
        }
    }
}
