using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using PrintVect.Core.Printing;
using PrintVect.Core.Protocol;

namespace PrintVect.Tests
{
    /// <summary>Records what the host asked it to print instead of touching System.Printing.</summary>
    internal sealed class FakePrintEngine : IPrintEngine
    {
        public readonly List<PrintRequest> Requests = new List<PrintRequest>();
        public PrintOutcome Outcome = PrintOutcome.Printed("Printed on Fake");
        public byte[] LastFileBytes;
        public TimeSpan Delay = TimeSpan.Zero;

        public PrintOutcome Print(PrintRequest request, Action<string, string> onProgress, CancellationToken ct)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }
            LastFileBytes = File.ReadAllBytes(request.FilePath);
            onProgress(JobStates.Printing, "Printing on " + request.FriendlyName);
            if (Delay > TimeSpan.Zero)
            {
                Thread.Sleep(Delay);
            }
            return Outcome;
        }
    }

    internal sealed class FakePrinters : IPrinterStatusSource
    {
        public IList<LocalPrinterInfo> GetPrinters()
        {
            return new List<LocalPrinterInfo>
            {
                new LocalPrinterInfo { Name = "Fake Printer", Status = PrinterStatuses.Ready, IsDefault = true },
                new LocalPrinterInfo { Name = "Other Printer", Status = PrinterStatuses.Offline }
            };
        }
    }
}
