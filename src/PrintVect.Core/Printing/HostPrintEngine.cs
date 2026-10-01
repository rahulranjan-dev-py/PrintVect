using System;
using System.Threading;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Printing
{
    /// <summary>
    /// The host's printing policy. .xps files go to the spooler directly (SpoolerXpsEngine); if the
    /// spooler refuses to even start the job, the XPS Print API is tried, then System.Printing.
    /// .oxps files go to System.Printing. A fallback only happens when nothing was sent yet, so a
    /// job is never printed twice.
    /// </summary>
    public sealed class HostPrintEngine : IPrintEngine
    {
        private readonly IPrintEngine _spooler;
        private readonly IPrintEngine _xpsPrint;
        private readonly IPrintEngine _systemPrinting;

        public HostPrintEngine() : this(new SpoolerXpsEngine(), new XpsPrintEngine(), new SystemPrintingEngine())
        {
        }

        public HostPrintEngine(IPrintEngine spooler, IPrintEngine xpsPrint, IPrintEngine systemPrinting)
        {
            if (spooler == null) throw new ArgumentNullException(nameof(spooler));
            if (xpsPrint == null) throw new ArgumentNullException(nameof(xpsPrint));
            if (systemPrinting == null) throw new ArgumentNullException(nameof(systemPrinting));
            _spooler = spooler;
            _xpsPrint = xpsPrint;
            _systemPrinting = systemPrinting;
        }

        public PrintOutcome Print(PrintRequest request, Action<string, string> onProgress, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (request.Format == JobFormats.Oxps)
            {
                Log.Info(request.JobId, "OpenXPS job: using System.Printing.");
                return _systemPrinting.Print(request, onProgress, ct);
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
