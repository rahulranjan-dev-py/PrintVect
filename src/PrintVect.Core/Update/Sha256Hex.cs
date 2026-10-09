using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace PrintVect.Core.Update
{
    /// <summary>SHA-256 as lower-case hex, the form the release's checksum file uses.</summary>
    public static class Sha256Hex
    {
        private const int BufferSize = 64 * 1024;
        private static readonly Regex HexPattern = new Regex(@"\b[0-9a-fA-F]{64}\b", RegexOptions.CultureInvariant);

        public static string OfFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize))
            {
                return ToHex(sha.ComputeHash(stream));
            }
        }

        public static string OfBytes(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                return ToHex(sha.ComputeHash(data));
            }
        }

        /// <summary>
        /// The first SHA-256 in a checksum file ("HEX  name", as sha256sum and the release workflow
        /// write it), lower-case; null when the text holds none.
        /// </summary>
        public static string ParseHashText(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            Match match = HexPattern.Match(text);
            return match.Success ? match.Value.ToLowerInvariant() : null;
        }

        public static bool Equal(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string ToHex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                text.Append(b.ToString("x2"));
            }
            return text.ToString();
        }
    }
}
