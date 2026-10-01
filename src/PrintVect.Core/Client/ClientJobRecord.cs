using System;
using System.Collections.Generic;
using System.Linq;

namespace PrintVect.Core.Client
{
    /// <summary>States of a job on the sending side, on top of the host's own states.</summary>
    public static class ClientJobStates
    {
        /// <summary>Taken from the spool folder, waiting to be sent.</summary>
        public const string Queued = "queued";
        /// <summary>Being transferred to the host.</summary>
        public const string Sending = "sending";
        /// <summary>The host has it and is printing.</summary>
        public const string Printing = "printing";
        public const string Printed = "printed";
        public const string Error = "error";
        /// <summary>The host could not be reached; the file waits for Retry.</summary>
        public const string Pending = "pending";

        public static bool IsFinal(string state)
        {
            return state == Printed || state == Error || state == Pending;
        }
    }

    /// <summary>One job sent (or waiting to be sent) from this PC.</summary>
    public sealed class ClientJobRecord
    {
        public string JobId { get; set; }
        public string PrinterId { get; set; }
        public string PrinterFriendly { get; set; }
        public string HostName { get; set; }
        public string Doc { get; set; }
        public string Format { get; set; }
        public long Size { get; set; }
        public string State { get; set; }
        public string Message { get; set; }
        public string FilePath { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public ClientJobRecord Clone()
        {
            return (ClientJobRecord)MemberwiseClone();
        }
    }

    /// <summary>In-memory list of the client's jobs (newest first). History across restarts comes with M3.</summary>
    public sealed class ClientJobTracker
    {
        public const int MaxRecords = 200;
        private readonly object _gate = new object();
        private readonly List<ClientJobRecord> _records = new List<ClientJobRecord>();

        /// <summary>Raised (on the caller's thread) with a copy of the record after every change.</summary>
        public event EventHandler<ClientJobRecord> Changed;

        public void Upsert(ClientJobRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            record.UpdatedAt = DateTime.Now;
            lock (_gate)
            {
                int index = _records.FindIndex(r => r.JobId == record.JobId);
                if (index >= 0)
                {
                    _records[index] = record.Clone();
                }
                else
                {
                    _records.Insert(0, record.Clone());
                    while (_records.Count > MaxRecords)
                    {
                        _records.RemoveAt(_records.Count - 1);
                    }
                }
            }
            EventHandler<ClientJobRecord> handler = Changed;
            if (handler != null)
            {
                handler(this, record.Clone());
            }
        }

        public IList<ClientJobRecord> Recent(int count)
        {
            lock (_gate)
            {
                return _records.Take(count).Select(r => r.Clone()).ToList();
            }
        }

        public ClientJobRecord Find(string jobId)
        {
            lock (_gate)
            {
                ClientJobRecord record = _records.FirstOrDefault(r => r.JobId == jobId);
                return record == null ? null : record.Clone();
            }
        }

        public int CountToday(string state)
        {
            DateTime today = DateTime.Today;
            lock (_gate)
            {
                return _records.Count(r => r.State == state && r.StartedAt >= today);
            }
        }
    }
}
