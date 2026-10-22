using System;
using System.Net;

namespace PrintVect.Core.Printing
{
    /// <summary>What sort of printer a Windows printer is, for the grey line under its name on Share my printers.</summary>
    public enum PrinterKind
    {
        Other,
        Pdf,
        Xps,
        OneNote,
        Fax,
        SavesFile,
        Usb,
        Network,
        Parallel,
        /// <summary>A printer connection to another computer's Windows share ("\\SERVER\Printer").</summary>
        WindowsShare
    }

    /// <summary>Tells the kind from the printer's name, port and driver (0.5.0); nothing here touches the spooler.</summary>
    public static class PrinterKinds
    {
        public static PrinterKind Classify(string name, string port, string driver)
        {
            string n = (name ?? "").Trim().ToLowerInvariant();
            string p = (port ?? "").Trim().ToUpperInvariant();
            string d = (driver ?? "").Trim().ToLowerInvariant();

            if (n.StartsWith(@"\\", StringComparison.Ordinal)) return PrinterKind.WindowsShare;
            if (n.Contains("onenote") || d.Contains("onenote")) return PrinterKind.OneNote;
            if (n.Contains("print to pdf") || d.Contains("print to pdf") || d.Contains("pdf writer")) return PrinterKind.Pdf;
            if (n.Contains("xps document writer") || d.Contains("xps document writer")) return PrinterKind.Xps;
            if (n == "fax" || n.StartsWith("fax ", StringComparison.Ordinal) || d.Contains("fax")) return PrinterKind.Fax;
            if (p == "PORTPROMPT:" || p == "FILE:" || p == "NUL:" || p == "NUL") return PrinterKind.SavesFile;
            if (p.StartsWith("USB", StringComparison.Ordinal) || p.StartsWith("DOT4", StringComparison.Ordinal)) return PrinterKind.Usb;
            if (p.StartsWith("LPT", StringComparison.Ordinal) || p.StartsWith("COM", StringComparison.Ordinal)) return PrinterKind.Parallel;
            IPAddress address;
            if (p.StartsWith("WSD", StringComparison.Ordinal) || p.StartsWith("IP_", StringComparison.Ordinal)
                || p.StartsWith("HTTP", StringComparison.Ordinal) || IPAddress.TryParse(p.Split(':')[0], out address))
            {
                return PrinterKind.Network;
            }
            return PrinterKind.Other;
        }

        /// <summary>"SERVER" of a "\\SERVER\Printer" connection, else null.</summary>
        public static string ShareServer(string name)
        {
            string n = (name ?? "").Trim();
            if (!n.StartsWith(@"\\", StringComparison.Ordinal)) return null;
            int slash = n.IndexOf('\\', 2);
            return slash > 2 ? n.Substring(2, slash - 2) : n.Substring(2);
        }
    }
}
