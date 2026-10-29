using System;
using System.Security.Cryptography;
using System.Text;

namespace PrintVect.Core.Config
{
    /// <summary>
    /// Keeps a secret in config.json unreadable on any other PC (0.5.8): Windows DPAPI with the machine's key, so it
    /// survives a change of the user who is logged in but means nothing in a copy of the file taken elsewhere.
    /// </summary>
    public static class MachineProtection
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PrintVect config 1");

        public static string Protect(byte[] secret)
        {
            if (secret == null) throw new ArgumentNullException(nameof(secret));
            return Convert.ToBase64String(ProtectedData.Protect(secret, Entropy, DataProtectionScope.LocalMachine));
        }

        /// <summary>The secret, or null when the text was not protected on this PC (copied from another one, or damaged).</summary>
        public static byte[] Unprotect(string protectedText)
        {
            if (string.IsNullOrWhiteSpace(protectedText))
            {
                return null;
            }
            try
            {
                return ProtectedData.Unprotect(Convert.FromBase64String(protectedText.Trim()), Entropy, DataProtectionScope.LocalMachine);
            }
            catch (FormatException)
            {
                return null;
            }
            catch (CryptographicException)
            {
                return null;
            }
        }
    }
}
