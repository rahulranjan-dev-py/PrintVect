using System;
using System.Security.Cryptography;
using System.Text;

namespace PrintVect.Core.Protocol
{
    /// <summary>The optional shared PIN travels as a SHA-256 hex digest, never in clear (brief, section 6).</summary>
    public static class PinHash
    {
        public static string Compute(string pin)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(pin ?? ""));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        /// <summary>True when the host has no PIN, or the header carries the digest of the host's PIN.</summary>
        public static bool Matches(string hostPin, string headerPinDigest)
        {
            if (string.IsNullOrEmpty(hostPin))
            {
                return true;
            }
            return string.Equals(Compute(hostPin), headerPinDigest ?? "", StringComparison.OrdinalIgnoreCase);
        }
    }
}
