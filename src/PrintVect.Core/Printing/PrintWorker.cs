using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Printing
{
    /// <summary>
    /// One background thread that prints received jobs one at a time, in arrival order (brief 5.3).
    /// It is an STA thread because System.Printing's XPS path needs one, and it is never the UI thread.
    /// </summary>
    public sealed class PrintWorker : IDisposable
    {
        private sealed class WorkItem
        {
            public PrintRequest Request;
            public Action<string, string> OnProgress;
            public TaskCompletionSource<PrintOutcome> Completion;
        }

        private readonly IPrintEngine _engine;
        private readonly BlockingCollection<WorkItem> _queue = new BlockingCollection<WorkItem>();
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        private Thread _thread;
        private int _pending;

        public PrintWorker(IPrintEngine engine)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            _engine = engine;
        }

        /// <summary>Jobs waiting or printing right now.</summary>
        public int PendingCount
        {
            get { return Volatile.Read(ref _pending); }
        }

        public void Start()
        {
            if (_thread != null)
            {
                return;
            }
            _thread = new Thread(Run) { IsBackground = true, Name = "PrintVect print worker" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
        }

        /// <summary>Queues a request; the task completes when the engine has an outcome.</summary>
        public Task<PrintOutcome> EnqueueAsync(PrintRequest request, Action<string, string> onProgress)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (_queue.IsAddingCompleted)
            {
                return Task.FromResult(PrintOutcome.Error("Sharing was turned off on this PC before the job could print."));
            }

            var item = new WorkItem
            {
                Request = request,
                OnProgress = onProgress ?? ((s, m) => { }),
                Completion = new TaskCompletionSource<PrintOutcome>()
            };
            Interlocked.Increment(ref _pending);
            _queue.Add(item);
            return item.Completion.Task;
        }

        /// <summary>Stops accepting jobs. The job being printed finishes; queued ones are still printed.</summary>
        public void Stop()
        {
            if (!_queue.IsAddingCompleted)
            {
                _queue.CompleteAdding();
            }
            _stopping.Cancel();
        }

        private void Run()
        {
            Log.Info("Print worker started.");
            foreach (WorkItem item in _queue.GetConsumingEnumerable())
            {
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
                    Interlocked.Decrement(ref _pending);
                }
                item.Completion.TrySetResult(outcome);
            }
            Log.Info("Print worker stopped.");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
