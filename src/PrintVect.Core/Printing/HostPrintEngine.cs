using System;
using System.Threading;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Printing
{
    /// <summary>
    /// The host's printing policy: .xps files go through the XPS Print API; .oxps files (and any
    /// .xps job the XPS Print API refuses to start) go through System.Printing's AddJob.
    /// </summary>
    public sealed class HostPrintEngine : IPrintEngine
    {
        private readonly IPrintEngine _xpsPrint;
        private readonly IPrintEngine _systemPrinting;

        public HostPrintEngine() : this(new XpsPrintEngine(), new SystemPrintingEngine())
        {
        }

        public HostPrintEngine(IPrintEngine xpsPrint, IPrintEngine systemPrinting)
        {
            if (xpsPrint == null) throw new ArgumentNullException(nameof(xpsPrint));
            if (systemPrinting == null) throw new ArgumentNullException(nameof(systemPrinting));
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
