using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Printing
{
    /// <summary>One queued print request with its completion.</summary>
    public sealed class PrintWorkItem
    {
        public PrintRequest Request;
        public Action<string, string> OnProgress;
        public TaskCompletionSource<PrintOutcome> Completion;
    }

    /// <summary>
    /// One background thread that prints the jobs for ONE printer, one at a time, in arrival
    /// order (brief 5.3). It is a multi-threaded-apartment thread (what the XPS Print API needs);
    /// an engine that needs a single-threaded apartment runs its call on one via ApartmentRunner.
    /// It is never the UI thread. A Windows call that never returns keeps this thread busy; the
    /// dispatcher then retires the worker and starts a fresh one for later jobs.
    /// </summary>
    public sealed class PrintWorker
    {
        private readonly IPrintEngine _engine;
        private readonly object _gate = new object();
        private readonly Queue<PrintWorkItem> _queue = new Queue<PrintWorkItem>();
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        private Thread _thread;
        private bool _closed;
        private PrintWorkItem _current;
        private DateTime _currentStartedUtc;

        public PrintWorker(IPrintEngine engine, string printerName)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            _engine = engine;
            PrinterName = printerName ?? "";
        }

        public string PrinterName { get; }

        /// <summary>Jobs waiting plus the one being printed.</summary>
        public int PendingCount
        {
            get { lock (_gate) { return _queue.Count + (_current == null ? 0 : 1); } }
        }

        /// <summary>The job inside Windows right now, or null.</summary>
        public string CurrentJobId
        {
            get { lock (_gate) { return _current == null ? null : _current.Request.JobId; } }
        }

        public TimeSpan? CurrentJobAge
        {
            get { lock (_gate) { return _current == null ? (TimeSpan?)null : DateTime.UtcNow - _currentStartedUtc; } }
        }

        public void Start()
        {
            if (_thread != null)
            {
                return;
            }
            _thread = new Thread(Run) { IsBackground = true, Name = "PrintVect print worker: " + PrinterName };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        /// <summary>Queues a request; the task completes when the engine has an outcome.</summary>
        public Task<PrintOutcome> EnqueueAsync(PrintRequest request, Action<string, string> onProgress)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var item = new PrintWorkItem
            {
                Request = request,
                OnProgress = onProgress ?? ((s, m) => { }),
                Completion = new TaskCompletionSource<PrintOutcome>()
            };
            if (!Enqueue(item))
            {
                item.Completion.TrySetResult(PrintOutcome.Error("Sharing was turned off on this PC before the job could print."));
            }
            return item.Completion.Task;
        }

        /// <summary>Queues an item taken from another (stuck) worker. False when this worker is closed.</summary>
        public bool Enqueue(PrintWorkItem item)
        {
            lock (_gate)
            {
                if (_closed)
                {
                    return false;
                }
                _queue.Enqueue(item);
                Monitor.PulseAll(_gate);
                return true;
            }
        }

        /// <summary>Stops taking new jobs. The current job finishes and queued ones are still printed.</summary>
        public void Stop()
        {
            lock (_gate)
            {
                _closed = true;
                Monitor.PulseAll(_gate);
            }
            _stopping.Cancel();
        }

        /// <summary>Closes the worker and hands back the jobs that have not started (its thread is stuck in Windows).</summary>
        public IList<PrintWorkItem> Retire()
        {
            lock (_gate)
            {
                _closed = true;
                var pending = new List<PrintWorkItem>(_queue);
                _queue.Clear();
                Monitor.PulseAll(_gate);
                return pending;
            }
        }

        private void Run()
        {
            Log.Info("Print worker started for \"" + PrinterName + "\".");
            while (true)
            {
                PrintWorkItem item;
                lock (_gate)
                {
                    while (_queue.Count == 0 && !_closed)
                    {
                        Monitor.Wait(_gate);
                    }
                    if (_queue.Count == 0)
                    {
                        break;
                    }
                    item = _queue.Dequeue();
                    _current = item;
                    _currentStartedUtc = DateTime.UtcNow;
                }

                PrintOutcome outcome;
                try
                {
                    outcome = _engine.Print(item.Request, item.OnProgress, _stopping.Token);
                }
                catch (Exception ex)
                {
                    Log.Error(item.Request.JobId, "Printing failed on \"" + item.Request.PrinterName + "\".", ex);
                    outcome = PrintOutcome.Error("Windows could not print this job on " + item.Request.FriendlyName
                                                 + ": " + ex.Message + " (see the host's log for details)");
                }
                finally
                {
                    lock (_gate)
                    {
                        _current = null;
                    }
                }
                item.Completion.TrySetResult(outcome);
            }
            Log.Info("Print worker stopped for \"" + PrinterName + "\".");
        }
    }
}
