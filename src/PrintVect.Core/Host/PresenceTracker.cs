using System;
using System.Collections.Generic;
using System.Linq;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Host
{
    /// <summary>One PC that uses this host's printers, as its last hello or job described it.</summary>
    public sealed class ClientPresence
    {
        public string Name { get; set; }
        public string Address { get; set; }
        /// <summary>The Windows edition the PC reported, or empty when it only sent jobs (PrintVect before 0.4.0).</summary>
        public string Windows { get; set; }
        public string AppVersion { get; set; }
        public string User { get; set; }
        public DateTime LastSeen { get; set; }

        public ClientPresence Clone()
        {
            return (ClientPresence)MemberwiseClone();
        }
    }

    /// <summary>
    /// The PCs that use this host (the "Computers" card of the design): every hello and every job marks its PC as seen.
    /// Kept in memory only; after a restart the list fills again within one hello interval.
    /// </summary>
    public sealed class PresenceTracker
    {
        /// <summary>A PC is shown as connected when it was heard from this recently (two hello intervals and a bit).</summary>
        public static readonly TimeSpan OnlineFor = TimeSpan.FromMinutes(12);

        private readonly object _gate = new object();
        private readonly Dictionary<string, ClientPresence> _byName = new Dictionary<string, ClientPresence>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Raised on the caller's thread after a PC was seen.</summary>
        public event EventHandler Changed;

        /// <summary>Notes that <paramref name="name"/> was heard from. Empty details keep what an earlier hello said.</summary>
        public void Seen(string name, string address, string windows, string appVersion, string user)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }
            bool isNew;
            lock (_gate)
            {
                ClientPresence presence;
                isNew = !_byName.TryGetValue(name.Trim(), out presence);
                if (isNew)
                {
                    presence = new ClientPresence { Name = name.Trim() };
                    _byName[presence.Name] = presence;
                }
                if (!string.IsNullOrWhiteSpace(address)) presence.Address = address;
                if (!string.IsNullOrWhiteSpace(windows)) presence.Windows = windows.Trim();
                if (!string.IsNullOrWhiteSpace(appVersion)) presence.AppVersion = appVersion.Trim();
                if (!string.IsNullOrWhiteSpace(user)) presence.User = user.Trim();
                presence.LastSeen = DateTime.Now;
            }
            if (isNew)
            {
                Log.Info("PC " + name + " uses this PC's printers (" + (string.IsNullOrWhiteSpace(windows) ? "Windows version not reported" : windows)
                         + (string.IsNullOrWhiteSpace(appVersion) ? "" : ", PrintVect " + appVersion) + ").");
            }
            EventHandler handler = Changed;
            if (handler != null)
            {
                try { handler(this, EventArgs.Empty); }
                catch (Exception ex) { Log.Error("A presence listener failed.", ex); }
            }
        }

        /// <summary>Every PC heard from since PrintVect started, by name.</summary>
        public IList<ClientPresence> All()
        {
            lock (_gate)
            {
                return _byName.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p => p.Clone()).ToList();
            }
        }

        public static bool IsOnline(ClientPresence presence, DateTime now)
        {
            return presence != null && now - presence.LastSeen <= OnlineFor;
        }
    }
}
