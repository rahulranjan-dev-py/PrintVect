using System;
using System.IO;
using System.Text;
using PrintVect.Core.Config;

namespace PrintVect.Core.Client
{
    /// <summary>
    /// Names for the client side: the virtual printer ("PrintVect - Counter 1 Laser @COUNTER1"), its
    /// spool folder (spool\{printerId}\) and the Local Port file inside it (brief 5.1).
    /// </summary>
    public static class ClientPrinterNames
    {
        public const string PrinterPrefix = "PrintVect - ";
        public const string PortFileName = "job.xps";
        public const string SentFolderName = "sent";
        public const string PendingFolderName = "pending";
        public const string FailedFolderName = "failed";
        public const int MaxPrinterNameLength = 100;

        /// <summary>Printer names must not contain backslashes or commas; everything else odd is dropped too.</summary>
        public static string PrinterName(string friendlyName, string hostName)
        {
            string friendly = Clean(friendlyName);
            string host = Clean(hostName);
            if (friendly.Length == 0) friendly = "Printer";
            string name = PrinterPrefix + friendly + (host.Length == 0 ? "" : " @" + host);
            if (name.Length > MaxPrinterNameLength)
            {
                name = name.Substring(0, MaxPrinterNameLength).TrimEnd();
            }
            return name;
        }

        public static bool IsPrintVectPrinter(string printerName)
        {
            return printerName != null && printerName.StartsWith(PrinterPrefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>spool\{printerId}\ : the printer id is 16 hex characters, so the path is always safe.</summary>
        public static string SpoolFolder(AppPaths paths, string printerId)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            return Path.Combine(paths.SpoolDir, SafeId(printerId));
        }

        public static string PortFile(AppPaths paths, string printerId)
        {
            return Path.Combine(SpoolFolder(paths, printerId), PortFileName);
        }

        public static string SafeId(string printerId)
        {
            if (string.IsNullOrWhiteSpace(printerId)) throw new ArgumentException("A printer id is required.", nameof(printerId));
            var sb = new StringBuilder();
            foreach (char c in printerId)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
            }
            if (sb.Length == 0) throw new ArgumentException("The printer id \"" + printerId + "\" has no usable characters.", nameof(printerId));
            return sb.ToString();
        }

        private static string Clean(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var sb = new StringBuilder();
            bool lastWasSpace = false;
            foreach (char c in text.Trim())
            {
                bool ok = char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.' || c == '(' || c == ')'
                          || c == '&' || c == '#' || c == '+' || c == '@';
                char use = ok ? c : ' ';
                if (use == ' ')
                {
                    if (lastWasSpace) continue;
                    lastWasSpace = true;
                }
                else
                {
                    lastWasSpace = false;
                }
                sb.Append(use);
            }
            return sb.ToString().Trim();
        }
    }
}
