using System;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace PrintVect.Core.Protocol
{
    /// <summary>
    /// A host's identity key as other PCs see it (0.5.8): the public half (an RSA CSP blob in base64) travels in list
    /// and discovery replies, and a client remembers its fingerprint (KeyIdOf) for each printer it adds. A PC
    /// name or printer id can be claimed by any PC; only the real host can sign a client's random challenge, together
    /// with its name and the address it answers on, with the key behind that fingerprint.
    /// </summary>
    public static class HostKeys
    {
        private const string Label = "PrintVect host 1";

        /// <summary>A fresh random challenge, 32 hex digits.</summary>
        public static string NewChallenge()
        {
            var bytes = new byte[16];
            using (var random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }
            return Hex(bytes);
        }

        /// <summary>The fingerprint a client remembers: SHA-256 of the public key, 64 hex digits. Null for a missing or broken key.</summary>
        public static string KeyIdOf(string publicKey)
        {
            byte[] blob = Decode(publicKey);
            if (blob == null)
            {
                return null;
            }
            using (var sha = SHA256.Create())
            {
                return Hex(sha.ComputeHash(blob));
            }
        }

        /// <summary>What the host signs: the challenge, its name and the address the asker reached it on.</summary>
        public static byte[] Message(string challenge, string hostName, string address)
        {
            return Encoding.UTF8.GetBytes(string.Join("\n", Label, challenge ?? "", (hostName ?? "").Trim().ToUpperInvariant(), NormalAddress(address)));
        }

        /// <summary>True when <paramref name="signature"/> is the holder of <paramref name="publicKey"/> signing that challenge, name and address.</summary>
        public static bool Verify(string publicKey, string challenge, string hostName, string address, string signature)
        {
            byte[] blob = Decode(publicKey);
            byte[] sig = Decode(signature);
            if (blob == null || sig == null || string.IsNullOrEmpty(challenge))
            {
                return false;
            }
            try
            {
                using (var rsa = new RSACryptoServiceProvider { PersistKeyInCsp = false })
                {
                    rsa.ImportCspBlob(blob);
                    return rsa.VerifyData(Message(challenge, hostName, address), sig, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                }
            }
            catch (CryptographicException)
            {
                return false;   // not an RSA public key: certainly not the host's
            }
        }

        /// <summary>"192.168.010.005" and "192.168.10.5" are the same address; a name stays as it is (upper case).</summary>
        internal static string NormalAddress(string address)
        {
            IPAddress parsed;
            string text = (address ?? "").Trim();
            if (IPAddress.TryParse(text, out parsed))
            {
                if (parsed.IsIPv4MappedToIPv6) parsed = parsed.MapToIPv4();
                return parsed.ToString();
            }
            return text.ToUpperInvariant();
        }

        private static byte[] Decode(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
            {
                return null;
            }
            try
            {
                return Convert.FromBase64String(base64.Trim());
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}
