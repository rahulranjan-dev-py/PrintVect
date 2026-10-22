using System;
using System.Globalization;
using PrintVect.App.Theme;
using PrintVect.Core.Client;
using PrintVect.Core.Protocol;
using PrintVect.Core.Printing;

namespace PrintVect.App.Forms
{
    /// <summary>The words and pill colours for printer statuses, job states and printer kinds, shared by the pages.</summary>
    internal static class Texts
    {
        public static string PrinterStatus(string status)
        {
            return Lookup("PrinterStatus_", string.IsNullOrEmpty(status) ? PrinterStatuses.Unknown : status);
        }

        public static PillKind PrinterPill(string status)
        {
            switch (status)
            {
                case PrinterStatuses.Ready:
                    return PillKind.Success;
                case PrinterStatuses.Busy:
                    return PillKind.Info;
                case PrinterStatuses.Paused:
                case PrinterStatuses.PaperOut:
                    return PillKind.Warning;
                case PrinterStatuses.Offline:
                case PrinterStatuses.Error:
                    return PillKind.Danger;
                default:
                    return PillKind.Neutral;
            }
        }

        public static string JobState(string state)
        {
            return Lookup("JobState_", state);
        }

        /// <summary>Green printed, yellow waiting, red failed, grey cancelled, blue on its way.</summary>
        public static PillKind JobPill(string state)
        {
            switch (state)
            {
                case JobStates.Printed:
                    return PillKind.Success;
                case JobStates.Error:
                    return PillKind.Danger;
                case ClientJobStates.Pending:
                    return PillKind.Warning;
                case ClientJobStates.Cancelled:
                    return PillKind.Neutral;
                default:
                    return PillKind.Info;
            }
        }

        /// <summary>"Software printer · saves a PDF file", "USB printer · default on this PC".</summary>
        public static string PrinterKind(LocalPrinterInfo printer)
        {
            string kind;
            switch (PrinterKinds.Classify(printer.Name, printer.PortName, printer.DriverName))
            {
                case Core.Printing.PrinterKind.Pdf: kind = Strings.KindPdf; break;
                case Core.Printing.PrinterKind.Xps: kind = Strings.KindXps; break;
                case Core.Printing.PrinterKind.OneNote: kind = Strings.KindOneNote; break;
                case Core.Printing.PrinterKind.Fax: kind = Strings.KindFax; break;
                case Core.Printing.PrinterKind.SavesFile: kind = Strings.KindFile; break;
                case Core.Printing.PrinterKind.Usb: kind = Strings.KindUsb; break;
                case Core.Printing.PrinterKind.Network: kind = Strings.KindNetwork; break;
                case Core.Printing.PrinterKind.Parallel: kind = string.Format(Strings.KindPort, printer.PortName); break;
                case Core.Printing.PrinterKind.WindowsShare: kind = string.Format(Strings.KindShare, PrinterKinds.ShareServer(printer.Name)); break;
                default: kind = string.IsNullOrEmpty(printer.PortName) ? "" : string.Format(Strings.KindPort, printer.PortName); break;
            }
            if (printer.IsDefault)
            {
                kind = kind.Length == 0 ? Strings.KindDefault : kind + " · " + Strings.KindDefault;
            }
            return kind;
        }

        /// <summary>"10:42" today, "21 Oct 10:42" before.</summary>
        public static string When(DateTime time)
        {
            return time.ToString(time.Date == DateTime.Today ? "HH:mm" : "dd MMM HH:mm", CultureInfo.CurrentCulture);
        }

        private static string Lookup(string prefix, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "";
            }
            string text = Strings.ResourceManager.GetString(prefix + key.Replace(' ', '_'), Strings.Culture);
            return string.IsNullOrEmpty(text) ? key : text;
        }
    }
}
