using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Printing
{
    /// <summary>
    /// One print worker per printer, created on demand, so a printer that does not answer never
    /// holds up the others. A worker whose thread is stuck inside Windows is retired: its waiting
    /// jobs move to a fresh worker, and the stuck thread is left to finish whenever Windows lets it.
    /// </summary>
    public sealed class PrintDispatcher : IDisposable
    {
        private readonly IPrintEngine _engine;
        private readonly object _gate = new object();
        private readonly Dictionary<string, PrintWorker> _workers = new Dictionary<string, PrintWorker>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PrintWorker> _retired = new List<PrintWorker>();
        private bool _stopped;

        public PrintDispatcher(IPrintEngine engine)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            _engine = engine;
        }

        public Task<PrintOutcome> EnqueueAsync(PrintRequest request, Action<string, string> onProgress)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            PrintWorker worker;
            lock (_gate)
            {
                if (_stopped)
                {
                    return Task.FromResult(PrintOutcome.Error("Sharing was turned off on this PC before the job could print."));
                }
                worker = GetOrCreate(request.PrinterName);
            }
            return worker.EnqueueAsync(request, onProgress);
        }

        /// <summary>Jobs waiting or printing on this printer.</summary>
        public int PendingCount(string printerName)
        {
            lock (_gate)
            {
                PrintWorker worker;
                return _workers.TryGetValue(printerName ?? "", out worker) ? worker.PendingCount : 0;
            }
        }

        /// <summary>Jobs waiting or printing on any printer, including stuck ones.</summary>
        public int PendingCountAll
        {
            get { lock (_gate) { return _workers.Values.Sum(w => w.PendingCount) + _retired.Sum(w => w.PendingCount); } }
        }

        public int StuckWorkerCount
        {
            get { lock (_gate) { return _retired.Count(w => w.CurrentJobId != null); } }
        }

        /// <summary>
        /// Called when <paramref name="stuckJobId"/> has been inside Windows for too long. Returns
        /// true when the worker was retired and a fresh one now serves that printer.
        /// </summary>
        public bool RetireStuckWorker(string printerName, string stuckJobId)
        {
            IList<PrintWorkItem> pending;
            PrintWorker replacement;
            lock (_gate)
            {
                PrintWorker worker;
                if (_stopped || !_workers.TryGetValue(printerName ?? "", out worker) || worker.CurrentJobId != stuckJobId)
                {
                    return false;
                }
                _workers.Remove(printerName);
                _retired.Add(worker);
                pending = worker.Retire();
                replacement = pending.Count == 0 ? null : GetOrCreate(printerName);
            }

            Log.Warn(stuckJobId, "Print thread for \"" + printerName + "\" is stuck inside Windows; retired it. "
                                 + pending.Count + " waiting job(s) moved to a new print thread.");
            if (replacement != null)
            {
                foreach (PrintWorkItem item in pending)
                {
                    if (!replacement.Enqueue(item))
                    {
                        item.Completion.TrySetResult(PrintOutcome.Error("Sharing was turned off on this PC before the job could print."));
                    }
                }
            }
            return true;
        }

        /// <summary>Stops taking new jobs; running and queued jobs still finish.</summary>
        public void Stop()
        {
            List<PrintWorker> all;
            lock (_gate)
            {
                _stopped = true;
                all = _workers.Values.Concat(_retired).ToList();
            }
            foreach (PrintWorker worker in all)
            {
                worker.Stop();
            }
        }

        public void Dispose()
        {
            Stop();
        }

        private PrintWorker GetOrCreate(string printerName)
        {
            string key = printerName ?? "";
            PrintWorker worker;
            if (!_workers.TryGetValue(key, out worker))
            {
                worker = new PrintWorker(_engine, key);
                worker.Start();
                _workers[key] = worker;
            }
            return worker;
        }
    }
}
