using System;
using System.Collections.Generic;
using System.Linq;

namespace PrintVect.Core.Host
{
    /// <summary>
    /// The nonces of PIN proofs this host accepted (0.5.9), each kept until its proof would be refused anyway (the
    /// proof's time plus the clock tolerance, PinProof). A request that comes again with a nonce already used is a
    /// recorded one sent again: refused. Every real send makes a new nonce, Retry included. Kept in memory only: after
    /// a restart a replayed job still cannot print twice, because the host answers a job id it knows with its state.
    /// </summary>
    public sealed class NonceMemory
    {
        public const int DefaultMaxEntries = 100000;

        private readonly Dictionary<string, DateTime> _until = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private readonly object _gate = new object();
        private DateTime _nextPrune = DateTime.MinValue;

        /// <summary>At most this many are kept; when full, the ones that expire soonest go first.</summary>
        public int MaxEntries { get; set; } = DefaultMaxEntries;

        public int Count
        {
            get { lock (_gate) { return _until.Count; } }
        }

        /// <summary>True when <paramref name="nonce"/> was seen before and has not expired.</summary>
        public bool WasUsed(string nonce, DateTime utcNow)
        {
            lock (_gate)
            {
                DateTime until;
                return _until.TryGetValue(nonce ?? "", out until) && until > utcNow;
            }
        }

        /// <summary>Remembers <paramref name="nonce"/> until <paramref name="untilUtc"/>. False when it was already used.</summary>
        public bool TryRemember(string nonce, DateTime untilUtc, DateTime utcNow)
        {
            nonce = nonce ?? "";
            lock (_gate)
            {
                DateTime until;
                if (_until.TryGetValue(nonce, out until) && until > utcNow)
                {
                    return false;
                }
                if (utcNow >= _nextPrune || _until.Count >= MaxEntries)
                {
                    Prune(utcNow);
                }
                _until[nonce] = untilUtc;
                return true;
            }
        }

        private void Prune(DateTime utcNow)
        {
            foreach (string expired in _until.Where(e => e.Value <= utcNow).Select(e => e.Key).ToList())
            {
                _until.Remove(expired);
            }
            int over = _until.Count - MaxEntries + 1;
            if (over > 0)
            {
                foreach (string soonest in _until.OrderBy(e => e.Value).Take(over).Select(e => e.Key).ToList())
                {
                    _until.Remove(soonest);
                }
            }
            _nextPrune = utcNow + TimeSpan.FromMinutes(10);
        }
    }
}
