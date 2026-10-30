using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PrintVect.Core.Protocol
{
    /// <summary>What a host found when it checked a request against its sharing PIN.</summary>
    public enum PinCheck
    {
        /// <summary>The host has no PIN: open to the network.</summary>
        Open,
        Accepted,
        /// <summary>No proof at all (the sender has no PIN set).</summary>
        Missing,
        /// <summary>Only the plain hash of PrintVect before 0.5.8: the sender must be updated.</summary>
        Outdated,
        /// <summary>The proof's time is more than ClockTolerance away from the host's clock.</summary>
        ClockOff,
        Wrong
    }

    /// <summary>
    /// The sharing PIN never travels, not even as a hash (0.5.8). A job or hello carries a one-time proof instead:
    /// PBKDF2-HMAC-SHA256 of the PIN, salted with the request's random nonce, its time and the job's printer, id and
    /// size. A recorded proof does not work for another job, and every guess at the PIN costs the guesser the same
    /// 20,000 rounds. Before 0.5.8 the header carried SHA-256 of the PIN, which anyone who saw it could reuse forever.
    /// A short PIN can still be guessed from recorded traffic with enough computing power: longer PINs are safer.
    /// </summary>
    public static class PinProof
    {
        public const int Iterations = 20000;

        /// <summary>How far the two PCs' clocks may differ. A recorded proof is refused after this.</summary>
        public static readonly TimeSpan ClockTolerance = TimeSpan.FromHours(24);

        private const string Label = "PrintVect PIN 1";

        /// <summary>Sets the header's nonce, time and proof for <paramref name="pin"/> (just before it is sent).</summary>
        public static void Sign(RequestHeader header, string pin, DateTime utcNow)
        {
            if (header == null) throw new ArgumentNullException(nameof(header));
            if (string.IsNullOrEmpty(pin))
            {
                header.Nonce = null;
                header.Time = 0;
                header.Proof = null;
                return;
            }
            header.Nonce = Guid.NewGuid().ToString("N");
            header.Time = UnixSeconds(utcNow);
            header.Proof = Compute(pin, header);
        }

        /// <summary>Checks a received header against the host's PIN.</summary>
        public static PinCheck Check(string hostPin, RequestHeader header, DateTime utcNow)
        {
            if (string.IsNullOrEmpty(hostPin))
            {
                return PinCheck.Open;
            }
            if (header == null || string.IsNullOrEmpty(header.Proof) || string.IsNullOrEmpty(header.Nonce) || header.Time <= 0)
            {
                return header != null && !string.IsNullOrEmpty(header.LegacyPinHash) ? PinCheck.Outdated : PinCheck.Missing;
            }
            long skew = Math.Abs(UnixSeconds(utcNow) - header.Time);
            if (skew > (long)ClockTolerance.TotalSeconds)
            {
                return PinCheck.ClockOff;
            }
            return FixedTimeEquals(Compute(hostPin, header), header.Proof.ToLowerInvariant()) ? PinCheck.Accepted : PinCheck.Wrong;
        }

        /// <summary>When a proof made at the header's time stops being accepted (UTC): its nonce need not be kept longer.</summary>
        public static DateTime ExpiresUtc(RequestHeader header)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(header.Time) + ClockTolerance;
        }

        /// <summary>The header's time as the host's local clock reads it, for messages.</summary>
        public static DateTime TimeOf(RequestHeader header)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(header.Time).ToLocalTime();
        }

        private static string Compute(string pin, RequestHeader header)
        {
            string salt = string.Join("\n", Label, header.Type ?? "", header.Nonce ?? "",
                header.Time.ToString(CultureInfo.InvariantCulture), header.PrinterId ?? "", header.JobId ?? "",
                header.Size.ToString(CultureInfo.InvariantCulture));
            using (var kdf = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(pin), Encoding.UTF8.GetBytes(salt), Iterations, HashAlgorithmName.SHA256))
            {
                byte[] proof = kdf.GetBytes(32);
                var sb = new StringBuilder(proof.Length * 2);
                foreach (byte b in proof)
                {
                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }

        private static long UnixSeconds(DateTime utc)
        {
            return (long)(utc.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        /// <summary>Compares without stopping at the first difference, so timing tells a guesser nothing.</summary>
        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length)
            {
                return false;
            }
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }
            return diff == 0;
        }
    }
}
