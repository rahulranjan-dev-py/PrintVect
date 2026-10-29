using System;
using System.Collections.Generic;
using System.Linq;

namespace PrintVect.Core.Host
{
    /// <summary>
    /// Wrong sharing PINs per sending address (0.5.8). After 5 wrong tries the address waits 15 minutes before it may
    /// try again, twice as long after each further 5 (at most 4 hours). A right PIN, or a day without a wrong one,
    /// forgets the address. Trying every 4-digit PIN this way takes about a year instead of seconds.
    /// </summary>
    public sealed class PinAttemptLimiter
    {
        public const int TriesBeforeWait = 5;
        public static readonly TimeSpan FirstWait = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan LongestWait = TimeSpan.FromHours(4);
        public static readonly TimeSpan ForgetAfter = TimeSpan.FromDays(1);
        private const int MaxAddresses = 1024;

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new object();

        /// <summary>How long <paramref name="address"/> must still wait before its PIN is checked again; zero when it may try.</summary>
        public TimeSpan WaitFor(string address, DateTime utcNow)
        {
            lock (_gate)
            {
                Entry entry = Find(address ?? "", utcNow);
                return entry == null || entry.WaitUntil <= utcNow ? TimeSpan.Zero : entry.WaitUntil - utcNow;
            }
        }

        /// <summary>Notes a wrong PIN. Returns the tries left before a wait; 0 means the address now waits.</summary>
        public int Failed(string address, DateTime utcNow)
        {
            address = address ?? "";
            lock (_gate)
            {
                Entry entry = Find(address, utcNow);
                if (entry == null)
                {
                    if (_entries.Count >= MaxAddresses)
                    {
                        _entries.Remove(_entries.OrderBy(e => e.Value.LastWrong).First().Key);
                    }
                    entry = new Entry();
                    _entries[address] = entry;
                }
                entry.Wrong++;
                entry.LastWrong = utcNow;
                if (entry.Wrong < TriesBeforeWait)
                {
                    return TriesBeforeWait - entry.Wrong;
                }
                double minutes = Math.Min(FirstWait.TotalMinutes * Math.Pow(2, entry.Waits), LongestWait.TotalMinutes);
                entry.WaitUntil = utcNow + TimeSpan.FromMinutes(minutes);
                entry.Waits++;
                entry.Wrong = 0;
                return 0;
            }
        }

        /// <summary>A right PIN: the address starts afresh.</summary>
        public void Succeeded(string address)
        {
            lock (_gate)
            {
                _entries.Remove(address ?? "");
            }
        }

        private Entry Find(string address, DateTime utcNow)
        {
            Entry entry;
            if (!_entries.TryGetValue(address, out entry))
            {
                return null;
            }
            if (entry.WaitUntil <= utcNow && utcNow - entry.LastWrong > ForgetAfter)
            {
                _entries.Remove(address);
                return null;
            }
            return entry;
        }

        private sealed class Entry
        {
            public int Wrong;
            public int Waits;
            public DateTime LastWrong;
            public DateTime WaitUntil;
        }
    }
}
