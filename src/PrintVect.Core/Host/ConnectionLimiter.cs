using System;
using System.Collections.Generic;
using System.Linq;

namespace PrintVect.Core.Host
{
    /// <summary>
    /// How many job-port connections a host serves at once (0.5.8): at most 32 in all and 8 from one address, and at most
    /// 120 new ones a minute from one address. A connection beyond that is closed at once, so one PC that opens
    /// connections in a loop cannot use up the host or fill its log. A PC that prints normally needs a few at most.
    /// </summary>
    public sealed class ConnectionLimiter
    {
        public const int DefaultMaxTotal = 32;
        public const int DefaultMaxPerAddress = 8;
        public const int DefaultMaxPerMinute = 120;

        private const int ForgetAbove = 256;
        private readonly Dictionary<string, Entry> _byAddress = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new object();
        private int _open;

        public int MaxTotal { get; set; } = DefaultMaxTotal;
        public int MaxPerAddress { get; set; } = DefaultMaxPerAddress;
        public int MaxPerMinute { get; set; } = DefaultMaxPerMinute;

        /// <summary>Null when the connection may be served (then Leave must follow), else why not.</summary>
        public string TryEnter(string address, DateTime utcNow)
        {
            address = address ?? "";
            lock (_gate)
            {
                if (_byAddress.Count > ForgetAbove)
                {
                    Forget(utcNow);
                }
                Entry entry;
                if (!_byAddress.TryGetValue(address, out entry))
                {
                    entry = new Entry();
                    _byAddress[address] = entry;
                }
                entry.Recent.RemoveAll(t => utcNow - t > TimeSpan.FromMinutes(1));
                if (_open >= MaxTotal)
                {
                    return MaxTotal + " connections are open already";
                }
                if (entry.Open >= MaxPerAddress)
                {
                    return MaxPerAddress + " connections from " + address + " are open already";
                }
                if (entry.Recent.Count >= MaxPerMinute)
                {
                    return MaxPerMinute + " connections from " + address + " in the last minute";
                }
                entry.Recent.Add(utcNow);
                entry.Open++;
                _open++;
                return null;
            }
        }

        public void Leave(string address)
        {
            lock (_gate)
            {
                Entry entry;
                if (_byAddress.TryGetValue(address ?? "", out entry) && entry.Open > 0)
                {
                    entry.Open--;
                    _open--;
                }
            }
        }

        /// <summary>Drops addresses with nothing open and nothing in the last minute (many addresses must not grow the table).</summary>
        private void Forget(DateTime utcNow)
        {
            foreach (Entry entry in _byAddress.Values)
            {
                entry.Recent.RemoveAll(t => utcNow - t > TimeSpan.FromMinutes(1));
            }
            foreach (string quiet in _byAddress.Where(e => e.Value.Open == 0 && e.Value.Recent.Count == 0).Select(e => e.Key).ToList())
            {
                _byAddress.Remove(quiet);
            }
        }

        public int OpenCount
        {
            get { lock (_gate) { return _open; } }
        }

        private sealed class Entry
        {
            public int Open;
            public readonly List<DateTime> Recent = new List<DateTime>();
        }
    }
}
