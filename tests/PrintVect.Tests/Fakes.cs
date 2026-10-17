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

        /// <summary>Printers whose jobs block inside Print until the event is set (a printer that never answers).</summary>
        public readonly Dictionary<string, ManualResetEventSlim> Blocked = new Dictionary<string, ManualResetEventSlim>(StringComparer.OrdinalIgnoreCase);

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
            ManualResetEventSlim gate;
            lock (Blocked)
            {
                Blocked.TryGetValue(request.PrinterName, out gate);
            }
            if (gate != null && !gate.Wait(TimeSpan.FromSeconds(30)))
            {
                return PrintOutcome.Error("test gate was never released");
            }
            return Outcome;
        }

        public ManualResetEventSlim Block(string printerName)
        {
            var gate = new ManualResetEventSlim(false);
            lock (Blocked)
            {
                Blocked[printerName] = gate;
            }
            return gate;
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
