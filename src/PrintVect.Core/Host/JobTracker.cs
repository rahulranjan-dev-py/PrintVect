using System;
using System.Collections.Generic;
using System.Linq;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Host
{
    /// <summary>What the host knows about one received job. Instances handed out are snapshots.</summary>
    public sealed class JobRecord
    {
        public string JobId { get; set; }
        public string PrinterId { get; set; }
        public string PrinterName { get; set; }
        public string PrinterFriendly { get; set; }
        public string Client { get; set; }
        public string User { get; set; }
        public string Doc { get; set; }
        public string Format { get; set; }
        public long Size { get; set; }
        public string State { get; set; }
        public string Message { get; set; }
        public DateTime ReceivedAt { get; set; }
        public DateTime? FinishedAt { get; set; }
        public string FilePath { get; set; }

        public JobRecord Clone()
        {
            return (JobRecord)MemberwiseClone();
        }
    }

    /// <summary>In-memory list of the jobs this host received (for status queries, the UI and the job count).</summary>
    public sealed class JobTracker
    {
        public const int MaxRecords = 200;

        private readonly object _gate = new object();
        private readonly Dictionary<string, JobRecord> _byId = new Dictionary<string, JobRecord>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _order = new Queue<string>();

        /// <summary>Raised with a snapshot after every add or update, on the caller's thread.</summary>
        public event EventHandler<JobRecord> Changed;

        public JobRecord Add(JobRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrEmpty(record.JobId)) throw new ArgumentException("A job id is required.", nameof(record));

            JobRecord snapshot;
            lock (_gate)
            {
                if (!_byId.ContainsKey(record.JobId))
                {
                    _order.Enqueue(record.JobId);
                }
                _byId[record.JobId] = record;
                while (_order.Count > MaxRecords)
                {
                    _byId.Remove(_order.Dequeue());
                }
                snapshot = record.Clone();
            }
            Raise(snapshot);
            return snapshot;
        }

        public JobRecord Update(string jobId, string state, string message)
        {
            JobRecord snapshot;
            lock (_gate)
            {
                JobRecord record;
                if (jobId == null || !_byId.TryGetValue(jobId, out record))
                {
                    return null;
                }
                record.State = state;
                record.Message = message ?? "";
                if (JobStates.IsFinal(state))
                {
                    record.FinishedAt = DateTime.Now;
                }
                snapshot = record.Clone();
            }
            Raise(snapshot);
            return snapshot;
        }

        public bool TryGet(string jobId, out JobRecord snapshot)
        {
            lock (_gate)
            {
                JobRecord record;
                if (jobId != null && _byId.TryGetValue(jobId, out record))
                {
                    snapshot = record.Clone();
                    return true;
                }
            }
            snapshot = null;
            return false;
        }

        /// <summary>Newest first.</summary>
        public IList<JobRecord> Recent(int count)
        {
            lock (_gate)
            {
                return _byId.Values.OrderByDescending(r => r.ReceivedAt).Take(count).Select(r => r.Clone()).ToList();
            }
        }

        public int CountToday(string state)
        {
            DateTime today = DateTime.Today;
            lock (_gate)
            {
                return _byId.Values.Count(r => r.State == state && (r.FinishedAt ?? r.ReceivedAt).Date == today);
            }
        }

        public int CountReceivedToday()
        {
            DateTime today = DateTime.Today;
            lock (_gate)
            {
                return _byId.Values.Count(r => r.ReceivedAt.Date == today);
            }
        }

        private void Raise(JobRecord snapshot)
        {
            EventHandler<JobRecord> handler = Changed;
            if (handler == null)
            {
                return;
            }
            try
            {
                handler(this, snapshot);
            }
            catch (Exception ex)
            {
                Log.Error(snapshot.JobId, "A job-changed listener failed.", ex);
            }
        }
    }
}
