using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core.Config;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Client
{
    /// <summary>How the client talks to a host. Tests replace it with a fake.</summary>
    public interface IJobSender
    {
        Task<JobReply> SendAsync(RemotePrinter printer, RequestHeader header, string filePath, CancellationToken ct);
        Task<JobReply> StatusAsync(RemotePrinter printer, string jobId, CancellationToken ct);
        Task<JobReply> HelloAsync(RemotePrinter printer, RequestHeader header, CancellationToken ct);
    }

    /// <summary>
    /// The real sender: TCP to the host's IP address first and, when that fails and a PC name is
    /// known, to the name (DHCP may have moved the host; brief 11.5). Any PC can answer to a name, so for a printer
    /// whose host key is remembered (0.5.8) the name is looked up and only an address that proves that key is used.
    /// Before a job leaves, the PC at the saved address must prove that key too (0.5.9): another PC may have been given
    /// the host's old address. A passed check holds for 5 minutes; a failed one counts as "not reachable there", so
    /// the job searches for the host and then waits for Retry, saying why.
    /// </summary>
    public sealed class TcpJobSender : IJobSender
    {
        public static readonly TimeSpan DefaultCheckPassedFor = TimeSpan.FromMinutes(5);

        private readonly Dictionary<string, DateTime> _passed = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        /// <summary>How long a passed identity check at an address is trusted before the next job checks again.</summary>
        public TimeSpan CheckPassedFor { get; set; } = DefaultCheckPassedFor;

        /// <summary>Raised on the sending thread when a printer added before 0.5.8 learned its host's key before a job (save config.json).</summary>
        public event EventHandler<RemotePrinter> HostKeyLearned;

        public async Task<JobReply> SendAsync(RemotePrinter printer, RequestHeader header, string filePath, CancellationToken ct)
        {
            if (printer == null) throw new ArgumentNullException(nameof(printer));
            string first, second;
            Addresses(printer, out first, out second);
            try
            {
                IPAddress literal;
                string target = IPAddress.TryParse(first, out literal) ? first : await CheckedAsync(printer, first, ct).ConfigureAwait(false);
                await EnsureHostAsync(printer, target, header.JobId, ct).ConfigureAwait(false);
                return await Client(target, printer).SendJobAsync(header, filePath, null, ct).ConfigureAwait(false);
            }
            catch (HostUnreachableException ex) when (second != null)
            {
                Log.Warn(header.JobId, first + " did not answer (" + ex.Message + "); trying the PC name " + second + ".");
                string checkedAddress = await CheckedAsync(printer, second, ct).ConfigureAwait(false);
                return await Client(checkedAddress, printer).SendJobAsync(header, filePath, null, ct).ConfigureAwait(false);
            }
        }

        public async Task<JobReply> StatusAsync(RemotePrinter printer, string jobId, CancellationToken ct)
        {
            if (printer == null) throw new ArgumentNullException(nameof(printer));
            string first, second;
            Addresses(printer, out first, out second);
            try
            {
                return await Client(first, printer).QueryStatusAsync(jobId, ct).ConfigureAwait(false);
            }
            catch (HostUnreachableException) when (second != null)
            {
                string checkedAddress = await CheckedAsync(printer, second, ct).ConfigureAwait(false);
                return await Client(checkedAddress, printer).QueryStatusAsync(jobId, ct).ConfigureAwait(false);
            }
        }

        public async Task<JobReply> HelloAsync(RemotePrinter printer, RequestHeader header, CancellationToken ct)
        {
            if (printer == null) throw new ArgumentNullException(nameof(printer));
            string first, second;
            Addresses(printer, out first, out second);
            try
            {
                return await Client(first, printer).SendHelloAsync(header, ct).ConfigureAwait(false);
            }
            catch (HostUnreachableException) when (second != null)
            {
                string checkedAddress = await CheckedAsync(printer, second, ct).ConfigureAwait(false);
                return await Client(checkedAddress, printer).SendHelloAsync(header, ct).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Makes sure the PC at <paramref name="address"/> is the printer's host before a job goes there (0.5.9): it must
        /// sign a challenge with the remembered key. A printer without one learns it here. Throws HostUnreachableException
        /// (with the reason) when the PC there does not answer or is not the host. Passed checks are trusted for a while.
        /// </summary>
        private async Task EnsureHostAsync(RemotePrinter printer, string address, string jobId, CancellationToken ct)
        {
            DateTime now = DateTime.UtcNow;
            string key = (printer.HostKeyId ?? "") + "|" + address + "|" + printer.Port;
            lock (_passed)
            {
                DateTime passedAt;
                if (_passed.TryGetValue(key, out passedAt) && now - passedAt < CheckPassedFor)
                {
                    return;
                }
            }
            MovedHostVerdict verdict = await MovedHostCheck.CheckAsync(printer, address, printer.Port, ct).ConfigureAwait(false);
            if (verdict.Problem != null)
            {
                Log.Warn(jobId, "Not sending to " + address + ": " + verdict.Problem);
                throw new HostUnreachableException(verdict.Problem + " Nothing was sent there.");
            }
            if (string.IsNullOrEmpty(printer.HostKeyId) && verdict.KeyId != null)
            {
                printer.HostKeyId = verdict.KeyId;
                Log.Info(jobId, "Remembered the identity key of " + printer.HostName + " (" + verdict.KeyId.Substring(0, 16) + "…) for \""
                                + printer.LocalPrinterName + "\".");
                RaiseHostKeyLearned(printer);
                key = printer.HostKeyId + "|" + address + "|" + printer.Port;
            }
            lock (_passed)
            {
                foreach (string old in _passed.Where(p => now - p.Value >= CheckPassedFor).Select(p => p.Key).ToList())
                {
                    _passed.Remove(old);
                }
                _passed[key] = now;
            }
            if (!string.IsNullOrEmpty(printer.HostKeyId))
            {
                Log.Info(jobId, "The PC at " + address + " proved it is " + printer.HostName + " (identity key checked; checked again after "
                                + CheckPassedFor.TotalMinutes + " minutes).");
            }
        }

        private void RaiseHostKeyLearned(RemotePrinter printer)
        {
            EventHandler<RemotePrinter> handler = HostKeyLearned;
            if (handler == null) return;
            try { handler(this, printer); }
            catch (Exception ex) { Log.Error("Saving the learned identity key of " + printer.HostName + " failed.", ex); }
        }

        /// <summary>
        /// The PC name as it is for a printer without a remembered host key (as before 0.5.8); else the first address
        /// the name leads to that proves the key (MovedHostCheck). Throws HostUnreachableException when none does.
        /// </summary>
        private static async Task<string> CheckedAsync(RemotePrinter printer, string name, CancellationToken ct)
        {
            IPAddress literal;
            if (string.IsNullOrEmpty(printer.HostKeyId) || IPAddress.TryParse(name, out literal))
            {
                return name;
            }
            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(name).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException || ex is ArgumentException)
            {
                throw new HostUnreachableException(printer.HostName + " did not answer at " + printer.HostIp + ", and its name leads nowhere (" + ex.Message + ").", ex);
            }
            string problem = null;
            foreach (IPAddress address in addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork))
            {
                string ip = address.ToString();
                if (string.Equals(ip, printer.HostIp, StringComparison.OrdinalIgnoreCase)) continue;   // just failed
                MovedHostVerdict verdict = await MovedHostCheck.CheckAsync(printer, ip, printer.Port, ct).ConfigureAwait(false);
                if (verdict.Problem == null) return ip;
                problem = verdict.Problem;
            }
            throw new HostUnreachableException(printer.HostName + " did not answer at " + printer.HostIp + (problem == null
                ? ", and its name leads to no other address."
                : ", and its name leads elsewhere: " + problem + " Nothing was sent there."));
        }

        private static JobClient Client(string address, RemotePrinter printer)
        {
            return new JobClient(address, printer.Port) { DisplayName = printer.HostName };
        }

        private static void Addresses(RemotePrinter printer, out string first, out string second)
        {
            string ip = (printer.HostIp ?? "").Trim();
            string name = (printer.HostName ?? "").Trim();
            if (ip.Length > 0)
            {
                first = ip;
                second = name.Length > 0 && !string.Equals(name, ip, StringComparison.OrdinalIgnoreCase) ? name : null;
            }
            else if (name.Length > 0)
            {
                first = name;
                second = null;
            }
            else
            {
                throw new InvalidOperationException("The printer \"" + printer.FriendlyName + "\" has no host address in config.json.");
            }
        }
    }
}
