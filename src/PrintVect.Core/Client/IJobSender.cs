using System;
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
    }

    /// <summary>
    /// The real sender: TCP to the host's IP address first and, when that fails and a PC name is
    /// known, to the name (DHCP may have moved the host; brief 11.5).
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
                return await new JobClient(first, printer.Port).SendJobAsync(header, filePath, null, ct).ConfigureAwait(false);
            }
            catch (HostUnreachableException ex) when (second != null)
            {
                Log.Warn(header.JobId, first + " did not answer (" + ex.Message + "); trying the PC name " + second + ".");
                return await new JobClient(second, printer.Port).SendJobAsync(header, filePath, null, ct).ConfigureAwait(false);
            }
        }

        public async Task<JobReply> StatusAsync(RemotePrinter printer, string jobId, CancellationToken ct)
        {
            if (printer == null) throw new ArgumentNullException(nameof(printer));
            string first, second;
            Addresses(printer, out first, out second);
            try
            {
                return await new JobClient(first, printer.Port).QueryStatusAsync(jobId, ct).ConfigureAwait(false);
            }
            catch (HostUnreachableException) when (second != null)
            {
                return await new JobClient(second, printer.Port).QueryStatusAsync(jobId, ct).ConfigureAwait(false);
            }
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
