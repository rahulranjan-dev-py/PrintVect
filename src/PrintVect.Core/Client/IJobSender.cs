using System;
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
    /// </summary>
    public sealed class TcpJobSender : IJobSender
    {
        public async Task<JobReply> SendAsync(RemotePrinter printer, RequestHeader header, string filePath, CancellationToken ct)
        {
            if (printer == null) throw new ArgumentNullException(nameof(printer));
            string first, second;
            Addresses(printer, out first, out second);
            try
            {
                return await Client(first, printer).SendJobAsync(header, filePath, null, ct).ConfigureAwait(false);
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
