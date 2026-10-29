using System;
using System.Security.Cryptography;
using PrintVect.Core.Config;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Host
{
    /// <summary>
    /// This PC's identity as a host (0.5.8): an RSA-2048 key made on first start and kept in config.json, protected
    /// for this PC (MachineProtection). The public half goes out with every list and discovery reply; a list request
    /// with a challenge gets it signed (HostKeys), so a client follows this PC to a new address only when it really is
    /// this PC.
    /// </summary>
    public sealed class HostIdentity : IDisposable
    {
        private readonly RSACryptoServiceProvider _rsa;
        private readonly object _gate = new object();

        private HostIdentity(RSACryptoServiceProvider rsa)
        {
            _rsa = rsa;
            PublicKey = Convert.ToBase64String(rsa.ExportCspBlob(false));
            KeyId = HostKeys.KeyIdOf(PublicKey);
        }

        /// <summary>The public key, base64 (what clients receive).</summary>
        public string PublicKey { get; }

        /// <summary>Its fingerprint (what clients remember).</summary>
        public string KeyId { get; }

        public static HostIdentity Create()
        {
            return new HostIdentity(new RSACryptoServiceProvider(2048) { PersistKeyInCsp = false });
        }

        /// <summary>
        /// The key kept in config.json (AppConfig.HostKey), or a new one when there is none or it cannot be read here;
        /// <paramref name="newProtectedKey"/> is then what config.json must keep from now on (else null).
        /// </summary>
        public static HostIdentity LoadOrCreate(string protectedKey, out string newProtectedKey)
        {
            newProtectedKey = null;
            byte[] blob = MachineProtection.Unprotect(protectedKey);
            if (blob != null)
            {
                try
                {
                    var rsa = new RSACryptoServiceProvider { PersistKeyInCsp = false };
                    rsa.ImportCspBlob(blob);
                    return new HostIdentity(rsa);
                }
                catch (CryptographicException ex)
                {
                    Log.Warn("This PC's identity key in config.json cannot be read (" + ex.Message + "); making a new one.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(protectedKey))
            {
                Log.Warn("The identity key in config.json was not made on this PC (copied from another one?); making a new one. "
                         + "If this PC's address changes, PCs that added its printers before must add them again.");
            }

            HostIdentity identity = Create();
            newProtectedKey = MachineProtection.Protect(identity._rsa.ExportCspBlob(true));
            Log.Info("Made this PC's identity key (fingerprint " + identity.KeyId.Substring(0, 16) + "…); other PCs remember it when they add a printer from here.");
            return identity;
        }

        /// <summary>Base64 signature of the challenge, this PC's name and the address the asker reached it on.</summary>
        public string Sign(string challenge, string hostName, string address)
        {
            lock (_gate)
            {
                return Convert.ToBase64String(_rsa.SignData(HostKeys.Message(challenge, hostName, address), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            }
        }

        public void Dispose()
        {
            _rsa.Dispose();
        }
    }
}
