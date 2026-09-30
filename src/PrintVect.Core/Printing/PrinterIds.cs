using System;
using System.Security.Cryptography;
using System.Text;

namespace PrintVect.Core.Printing
{
    /// <summary>
    /// Stable id of a shared printer: derived from the host PC name and the Windows printer name,
    /// so it survives restarts, config edits and un-sharing/re-sharing, and clients keep working.
    /// </summary>
    public static class PrinterIds
    {
        public static string For(string machineName, string localPrinterName)
        {
            string text = (machineName ?? "").Trim().ToUpperInvariant() + "|" + (localPrinterName ?? "").Trim();
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                var sb = new StringBuilder(16);
                for (int i = 0; i < 8; i++)
                {
                    sb.Append(hash[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}
