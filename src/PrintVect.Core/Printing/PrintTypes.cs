using System;
using System.Collections.Generic;
using System.Threading;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Printing
{
    /// <summary>One received file that must go to one local printer.</summary>
    public sealed class PrintRequest
    {
        public string JobId { get; set; }
        /// <summary>The Windows printer name on this PC.</summary>
        public string PrinterName { get; set; }
        public string FriendlyName { get; set; }
        public string FilePath { get; set; }
        /// <summary>"xps" or "oxps".</summary>
        public string Format { get; set; }
        public string DocumentName { get; set; }
        public string ClientName { get; set; }
        public string UserName { get; set; }
        /// <summary>How many times the document is printed (each copy is its own Windows job).</summary>
        public int Copies { get; set; } = 1;
    }

    /// <summary>What happened to a print request: printed, error, or still printing after the wait.</summary>
    public sealed class PrintOutcome
    {
        public string State { get; private set; }
        public string Message { get; private set; }

        public static PrintOutcome Printed(string message)
        {
            return new PrintOutcome { State = JobStates.Printed, Message = message };
        }

        public static PrintOutcome Error(string message)
        {
            return new PrintOutcome { State = JobStates.Error, Message = message };
        }

        public static PrintOutcome StillPrinting(string message)
        {
            return new PrintOutcome { State = JobStates.Printing, Message = message };
        }
    }

    /// <summary>
    /// Prints one request and blocks until it is printed, failed, or the wait runs out. Called on the
    /// print worker thread (STA). The real one uses System.Printing; tests use a fake.
    /// <paramref name="onProgress"/> reports (state, message) while the job moves.
    /// </summary>
    public interface IPrintEngine
    {
        PrintOutcome Print(PrintRequest request, Action<string, string> onProgress, CancellationToken ct);
    }

    /// <summary>A printer installed on this PC.</summary>
    public sealed class LocalPrinterInfo
    {
        public string Name { get; set; }
        /// <summary>ready, busy, offline, paper out, paused, error or unknown.</summary>
        public string Status { get; set; }
        public bool IsDefault { get; set; }
    }

    /// <summary>Names printer statuses the same way everywhere (protocol, UI, log).</summary>
    public static class PrinterStatuses
    {
        public const string Ready = "ready";
        public const string Busy = "busy";
        public const string Offline = "offline";
        public const string PaperOut = "paper out";
        public const string Paused = "paused";
        public const string Error = "error";
        public const string Unknown = "unknown";
    }

    /// <summary>Lists the printers on this PC. The real one uses System.Printing; tests use a fake.</summary>
    public interface IPrinterStatusSource
    {
        IList<LocalPrinterInfo> GetPrinters();
    }
}
