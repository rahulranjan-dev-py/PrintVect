using System;
using System.Threading;
using PrintVect.Core.Diagnostics;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Printing
{
    /// <summary>
    /// The host's printing policy. .xps files, and on Windows 8 or later .oxps files too, go to the
    /// spooler directly (SpoolerXpsEngine); if the spooler refuses to even start the job, the XPS
    /// Print API is tried, then System.Printing. On Windows 7 .oxps goes to System.Printing (which
    /// explains that a Windows 7 host cannot print OpenXPS). Printers on a port that asks the host
    /// user for a file name also use System.Printing (PORTPROMPT: or FILE:, that is Microsoft Print
    /// to PDF and the XPS Document Writer):
    /// seen on the owner's PC, the direct spooler path saved an unreadable PDF to Documents without the
    /// Save window, while System.Printing showed the window and produced a readable file.
    /// A fallback only happens when nothing was sent yet, so a job is never printed twice.
    /// </summary>
    public sealed class HostPrintEngine : IPrintEngine
    {
        private readonly IPrintEngine _spooler;
        private readonly IPrintEngine _xpsPrint;
        private readonly IPrintEngine _systemPrinting;
        private readonly Func<string, string, string> _portLookup;
        private readonly bool _spoolerPrintsOpenXps;

        public HostPrintEngine()
            : this(new SpoolerXpsEngine(), new XpsPrintEngine(), new SystemPrintingEngine(), PrinterPorts.GetPortName,
                   WindowsInfo.IsWindows8OrLater)
        {
        }

        /// <summary>For tests: no port lookup, every printer counts as a paper printer, OpenXPS goes to System.Printing.</summary>
        public HostPrintEngine(IPrintEngine spooler, IPrintEngine xpsPrint, IPrintEngine systemPrinting)
            : this(spooler, xpsPrint, systemPrinting, (printer, jobId) => null, false)
        {
        }

        /// <param name="portLookup">(printer name, jobId) to the printer's Windows port name, or null when unknown.</param>
        /// <param name="spoolerPrintsOpenXps">True on Windows 8 or later, where the spooler converts OpenXPS itself.</param>
        public HostPrintEngine(IPrintEngine spooler, IPrintEngine xpsPrint, IPrintEngine systemPrinting,
                               Func<string, string, string> portLookup, bool spoolerPrintsOpenXps)
        {
            if (spooler == null) throw new ArgumentNullException(nameof(spooler));
            if (xpsPrint == null) throw new ArgumentNullException(nameof(xpsPrint));
            if (systemPrinting == null) throw new ArgumentNullException(nameof(systemPrinting));
            if (portLookup == null) throw new ArgumentNullException(nameof(portLookup));
            _spooler = spooler;
            _xpsPrint = xpsPrint;
            _systemPrinting = systemPrinting;
            _portLookup = portLookup;
            _spoolerPrintsOpenXps = spoolerPrintsOpenXps;
        }

        public PrintOutcome Print(PrintRequest request, Action<string, string> onProgress, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (request.Format == JobFormats.Oxps && !_spoolerPrintsOpenXps)
            {
                Log.Info(request.JobId, "OpenXPS job on a Windows older than 8: using System.Printing.");
                return _systemPrinting.Print(request, onProgress, ct);
            }

            string port = _portLookup(request.PrinterName, request.JobId);
            if (PrinterPorts.IsPromptingPort(port))
            {
                Log.Info(request.JobId, "Printer \"" + request.PrinterName + "\" is on port " + port.Trim()
                                        + ", which asks for a file name on this PC: using System.Printing so that the Save window appears.");
                return _systemPrinting.Print(request, onProgress, ct);
            }
            if (!string.IsNullOrEmpty(port))
            {
                Log.Info(request.JobId, "Printer \"" + request.PrinterName + "\" is on port " + port.Trim() + ": using the spooler directly.");
            }

            try
            {
                return _spooler.Print(request, onProgress, ct);
            }
            catch (SpoolerStartException ex)
            {
                Log.Warn(request.JobId, "The spooler would not start the job (" + ex.Message + "); trying the XPS Print API.");
            }

            try
            {
                return _xpsPrint.Print(request, onProgress, ct);
            }
            catch (XpsPrintStartException ex)
            {
                Log.Warn(request.JobId, "XPS Print API could not start the job (" + ex.Message + "); falling back to System.Printing.");
                return _systemPrinting.Print(request, onProgress, ct);
            }
        }
    }
}
