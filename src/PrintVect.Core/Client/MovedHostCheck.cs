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

        /// <summary>The PC there did not answer the check at all (switched off, moved): not a matter of identity.</summary>
        public bool Unreachable { get; set; }

        /// <summary>
        /// 0.5.10: the PC there proved, with a valid signature for that address, a key other than the remembered one:
        /// PrintVect or Windows was installed again there, or another PC got the address. NewKeyId is that key.
        /// </summary>
        public bool KeyChanged { get; set; }

        public string NewKeyId { get; set; }

        /// <summary>The PC name the PC there gave, for messages ("which calls itself …").</summary>
        public string AnsweredName { get; set; }
    }

    /// <summary>
    /// The PC at a printer's address answered but did not prove it is the PC the printer was added from (0.5.10). Unlike
    /// HostUnreachableException, its message is the reason itself and must reach the person as it is.
    /// </summary>
    public sealed class HostIdentityException : Exception
    {
        public HostIdentityException(string message, bool keyChanged) : base(message)
        {
            KeyChanged = keyChanged;
        }

        /// <summary>A different key was proven (Trust it again can take it), not just no proof.</summary>
        public bool KeyChanged { get; }
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
                return new MovedHostVerdict
                {
                    Problem = "the PC at " + address + " did not answer the identity check (" + ex.Message + ").",
                    Unreachable = true
                };
            }
            return Judge(printer, address, challenge, reply);
        }

        /// <summary>The decision itself, for a reply already received. Problem null: follow.</summary>
        public static MovedHostVerdict Judge(RemotePrinter printer, string address, string challenge, ListReply reply)
        {
            string host = string.IsNullOrWhiteSpace(printer.HostName) ? "the PC with this printer" : printer.HostName;
            string answered = reply == null || string.IsNullOrWhiteSpace(reply.Host) ? null : reply.Host.Trim();
            string claimed = answered == null ? "A PC" : "A PC calling itself " + answered;
            var verdict = new MovedHostVerdict { AnsweredName = answered };
            if (reply == null || !reply.Ok)
            {
                verdict.Problem = "the PC at " + address + " refused the identity check" + (reply == null || string.IsNullOrEmpty(reply.Message) ? "." : ": " + reply.Message);
                return verdict;
            }
            if (string.IsNullOrEmpty(reply.HostKey))
            {
                verdict.Problem = string.IsNullOrEmpty(printer.HostKeyId)
                    ? null   // a host before 0.5.8 and a printer that never learned a key: as before
                    : claimed + " answers at " + address + " but cannot prove it is " + host + ", the PC this printer was added from.";
                return verdict;
            }
            string offered = HostKeys.KeyIdOf(reply.HostKey);
            bool signed = HostKeys.Verify(reply.HostKey, challenge, reply.Host, address, reply.HostSig);
            if (!string.IsNullOrEmpty(printer.HostKeyId) && !string.Equals(offered, printer.HostKeyId, StringComparison.OrdinalIgnoreCase))
            {
                if (signed)
                {
                    // A real key, properly proven for this address, just not the one remembered (0.5.10).
                    verdict.KeyChanged = true;
                    verdict.NewKeyId = offered;
                    verdict.Problem = (answered == null ? "The PC" : "The PC " + answered) + " at " + address + " proved a different identity key than "
                                      + host + " had when this printer was added: PrintVect or Windows was installed again there, or it is another PC.";
                    return verdict;
                }
                verdict.Problem = claimed + " answers at " + address + ", but it is not " + host + ", the PC this printer was added from (its identity key differs).";
                return verdict;
            }
            if (!signed)
            {
                verdict.Problem = claimed + " answers at " + address + " but did not prove it is " + host + " (its identity check failed).";
                return verdict;
            }
            verdict.KeyId = offered;
            return verdict;
        }
    }
}
