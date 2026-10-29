using System;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core.Config;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Client
{
    /// <summary>What a search for a host that did not answer found (ClientService.FindHost).</summary>
    public sealed class HostSearchOutcome
    {
        public static readonly HostSearchOutcome NotFound = new HostSearchOutcome(false, null);
        public static readonly HostSearchOutcome Followed = new HostSearchOutcome(true, null);

        private HostSearchOutcome(bool moved, string refusal)
        {
            Moved = moved;
            Refusal = refusal;
        }

        /// <summary>The printer now carries the host's new address.</summary>
        public bool Moved { get; }

        /// <summary>A PC answered for the host but did not prove it is that host: why, in plain words. Null otherwise.</summary>
        public string Refusal { get; }

        public static HostSearchOutcome Refused(string reason)
        {
            return new HostSearchOutcome(false, reason);
        }
    }

    /// <summary>The answer of MovedHostCheck: Problem is null when the printer may follow; KeyId is the fingerprint proven.</summary>
    public sealed class MovedHostVerdict
    {
        public string Problem { get; set; }
        public string KeyId { get; set; }
    }

    /// <summary>
    /// Whether a printer may follow its host to a new address (0.5.8). Any PC can answer discovery with the host's
    /// PC name and printer ids, which travel openly, so a PC that claims them must also sign a random challenge,
    /// together with its name and the address it answers on, with the identity key remembered for the printer
    /// (RemotePrinter.HostKeyId; HostKeys). The same check at the printer's own address is how a printer learns the
    /// key: when it is added, or, for printers added before 0.5.8, after the host answers a hello. A printer with no
    /// key yet remembers the first one proven to it; an older host without a key is followed as before 0.5.8.
    /// </summary>
    public static class MovedHostCheck
    {
        /// <summary>
        /// Asks the PC at <paramref name="address"/> to prove it is the printer's host. The verdict's Problem is null when
        /// the printer may follow it; its KeyId is the fingerprint to remember when the printer had none.
        /// </summary>
        public static async Task<MovedHostVerdict> CheckAsync(RemotePrinter printer, string address, int port, CancellationToken ct)
        {
            if (printer == null) throw new ArgumentNullException(nameof(printer));
            string challenge = HostKeys.NewChallenge();
            ListReply reply;
            try
            {
                reply = await new JobClient(address, port) { DisplayName = printer.HostName }.ListPrintersAsync(ct, challenge).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new MovedHostVerdict { Problem = "the PC at " + address + " did not answer the identity check (" + ex.Message + ")." };
            }
            string keyId;
            string problem = Judge(printer, address, challenge, reply, out keyId);
            return new MovedHostVerdict { Problem = problem, KeyId = keyId };
        }

        /// <summary>The decision itself, for a reply already received. Null: follow.</summary>
        public static string Judge(RemotePrinter printer, string address, string challenge, ListReply reply, out string keyId)
        {
            keyId = null;
            string host = string.IsNullOrWhiteSpace(printer.HostName) ? "the PC with this printer" : printer.HostName;
            string claimed = reply == null || string.IsNullOrWhiteSpace(reply.Host) ? "A PC" : "A PC calling itself " + reply.Host.Trim();
            if (reply == null || !reply.Ok)
            {
                return "the PC at " + address + " refused the identity check" + (reply == null || string.IsNullOrEmpty(reply.Message) ? "." : ": " + reply.Message);
            }
            if (string.IsNullOrEmpty(reply.HostKey))
            {
                return string.IsNullOrEmpty(printer.HostKeyId)
                    ? null   // a host before 0.5.8 and a printer that never learned a key: as before
                    : claimed + " answers at " + address + " but cannot prove it is " + host + ", the PC this printer was added from.";
            }
            string offered = HostKeys.KeyIdOf(reply.HostKey);
            if (!string.IsNullOrEmpty(printer.HostKeyId) && !string.Equals(offered, printer.HostKeyId, StringComparison.OrdinalIgnoreCase))
            {
                return claimed + " answers at " + address + ", but it is not " + host + ", the PC this printer was added from (its identity key differs).";
            }
            if (!HostKeys.Verify(reply.HostKey, challenge, reply.Host, address, reply.HostSig))
            {
                return claimed + " answers at " + address + " but did not prove it is " + host + " (its identity check failed).";
            }
            keyId = offered;
            return null;
        }
    }
}
