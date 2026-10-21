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
        public int Copies { get; set; } = 1;
        public string State { get; set; }
        public string Message { get; set; }
        public DateTime ReceivedAt { get; set; }
        /// <summary>Insertion order inside the tracker: breaks ties when two jobs arrive within the same clock tick.</summary>
        public long Sequence { get; set; }
        public DateTime? FinishedAt { get; set; }
        public string FilePath { get; set; }

        /// <summary>The job never printed because PrintVect here was closed or sharing went off: the sender may send it again.</summary>
        public bool Lost { get; set; }

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
        private long _sequence;
        private Queue<string> _order = new Queue<string>();

        /// <summary>Raised with a snapshot after every add or update, on the caller's thread.</summary>
        public event EventHandler<JobRecord> Changed;

        public JobRecord Add(JobRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            if (string.IsNullOrEmpty(record.JobId)) throw new ArgumentException("A job id is required.", nameof(record));

            JobRecord snapshot;
            lock (_gate)
            {
                record.Sequence = ++_sequence;
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

        /// <summary>
        /// Puts the jobs of an earlier run back (oldest first). A job that was not finished when
        /// PrintVect closed is marked as failed with a plain explanation. Raises no events.
        /// </summary>
        public int Restore(IEnumerable<JobRecord> records)
        {
            if (records == null) return 0;
            int restored = 0;
            lock (_gate)
            {
                foreach (JobRecord record in records.Where(r => r != null && !string.IsNullOrEmpty(r.JobId))
                                                     .OrderBy(r => r.ReceivedAt).ThenBy(r => r.Sequence))
                {
                    if (_byId.ContainsKey(record.JobId)) continue;
                    if (!JobStates.IsFinal(record.State))
                    {
                        record.State = JobStates.Error;
                        record.Message = "PrintVect on " + Environment.MachineName + " was closed before this job finished.";
                        record.Lost = true;
                        if (record.FinishedAt == null) record.FinishedAt = record.ReceivedAt;
                    }
                    record.Sequence = ++_sequence;
                    _byId[record.JobId] = record;
                    _order.Enqueue(record.JobId);
                    restored++;
                }
                while (_order.Count > MaxRecords)
                {
                    _byId.Remove(_order.Dequeue());
                }
            }
            return restored;
        }

        /// <summary>Every job, newest first (what the history file stores).</summary>
        public IList<JobRecord> All()
        {
            return Recent(MaxRecords);
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

        /// <summary>Marks a job as failed because it was never printed here; a status request then invites the sender to send it again.</summary>
        public JobRecord MarkLost(string jobId, string message)
        {
            lock (_gate)
            {
                JobRecord record;
                if (_byId.TryGetValue(jobId ?? "", out record))
                {
                    record.Lost = true;
                }
            }
            return Update(jobId, JobStates.Error, message);
        }

        /// <summary>Raised (on the caller's thread) with the job id after a job was taken off the list.</summary>
        public event EventHandler<string> Removed;

        /// <summary>Takes a printed or failed job off the list. A job still waiting or printing stays (false).</summary>
        public bool Remove(string jobId)
        {
            return RemoveWhere(r => string.Equals(r.JobId, jobId, StringComparison.OrdinalIgnoreCase)) == 1;
        }

        /// <summary>Takes every printed and failed job off the list; waiting and printing jobs stay.</summary>
        public int RemoveFinished()
        {
            return RemoveWhere(r => true);
        }

        private int RemoveWhere(Func<JobRecord, bool> match)
        {
            List<string> removed;
            lock (_gate)
            {
                removed = _byId.Values.Where(r => JobStates.IsFinal(r.State) && match(r)).Select(r => r.JobId).ToList();
                foreach (string jobId in removed)
                {
                    _byId.Remove(jobId);
                }
                _order = new Queue<string>(_order.Where(id => _byId.ContainsKey(id)));
            }
            EventHandler<string> handler = Removed;
            if (handler != null)
            {
                foreach (string jobId in removed)
                {
                    try { handler(this, jobId); }
                    catch (Exception ex) { Log.Error(jobId, "A job-removed listener failed.", ex); }
                }
            }
            return removed.Count;
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
                return _byId.Values.OrderByDescending(r => r.ReceivedAt).ThenByDescending(r => r.Sequence).Take(count).Select(r => r.Clone()).ToList();
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
