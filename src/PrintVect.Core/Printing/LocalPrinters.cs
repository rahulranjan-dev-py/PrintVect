using System;
using System.Collections.Generic;
using System.Linq;
using System.Printing;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Printing
{
    /// <summary>
    /// The printers installed on this PC, read with System.Printing. Slow on a busy spooler,
    /// so never call it on the UI thread (brief 11.6).
    /// </summary>
    public sealed class LocalPrinters : IPrinterStatusSource
    {
        public IList<LocalPrinterInfo> GetPrinters()
        {
            var result = new List<LocalPrinterInfo>();
            string defaultName = DefaultPrinterName();

            using (var server = new LocalPrintServer())
            {
                var types = new[] { EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections };
                foreach (PrintQueue queue in server.GetPrintQueues(types))
                {
                    using (queue)
                    {
                        var info = new LocalPrinterInfo { Name = queue.FullName, Status = PrinterStatuses.Unknown };
                        try
                        {
                            queue.Refresh();
                            info.Status = DescribeStatus(queue);
                        }
                        catch (Exception ex)
                        {
                            Log.Warn("Printer \"" + info.Name + "\" did not report its status: " + ex.Message);
                        }
                        info.IsDefault = string.Equals(info.Name, defaultName, StringComparison.OrdinalIgnoreCase);
                        result.Add(info);
                    }
                }
            }

            return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string DefaultPrinterName()
        {
            try
            {
                using (PrintQueue queue = LocalPrintServer.GetDefaultPrintQueue())
                {
                    return queue.FullName;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("No default printer could be read: " + ex.Message);
                return null;
            }
        }

        public static string DescribeStatus(PrintQueue queue)
        {
            if (queue.IsOffline) return PrinterStatuses.Offline;
            if (queue.IsOutOfPaper) return PrinterStatuses.PaperOut;
            if (queue.IsInError || queue.HasPaperProblem || queue.IsDoorOpened || queue.IsPaperJammed
                || queue.IsOutOfMemory || queue.NeedUserIntervention || queue.IsNotAvailable)
            {
                return PrinterStatuses.Error;
            }
            if (queue.IsPaused) return PrinterStatuses.Paused;
            if (queue.IsPrinting || queue.IsBusy || queue.IsProcessing || queue.NumberOfJobs > 0) return PrinterStatuses.Busy;
            return PrinterStatuses.Ready;
        }
    }
}
