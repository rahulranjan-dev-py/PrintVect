using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Printing
{
    /// <summary>The XPS Print API refused to start the job (no spooler job exists); the caller may fall back.</summary>
    public sealed class XpsPrintStartException : Exception
    {
        public XpsPrintStartException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Prints an .xps file with the Windows XPS Print API (xpsprint.dll, Windows 7 SP1 and later).
    /// The file goes straight to the spooler for any printer, XPS-based or GDI-based; Windows converts
    /// as needed. Unlike System.Printing's AddJob, nothing blocks inside a driver conversion: the job
    /// is visible in the Windows print queue at once, and progress and completion are reported by
    /// the API. Chosen after AddJob hung for the owner's HP Laser without ever creating a spooler job.
    /// </summary>
    public sealed class XpsPrintEngine : IPrintEngine
    {
        /// <summary>How long one job may keep its printer's print thread before the thread moves on.</summary>
        public static readonly TimeSpan CompletionWait = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan SettleWait = TimeSpan.FromSeconds(5);
        private const int CopyBufferSize = 64 * 1024;

        public PrintOutcome Print(PrintRequest request, Action<string, string> onProgress, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string jobId = request.JobId;
            string host = Environment.MachineName;
            string jobName = BuildJobName(request);

            IXpsPrintJob job = null;
            IXpsPrintJobStream documentStream = null;
            IXpsPrintJobStream printTicketStream = null;
            using (var completion = new ManualResetEvent(false))
            {
                try
                {
                    onProgress(JobStates.Printing, "Windows is sending the job to " + request.FriendlyName + " on " + host
                                                   + ". If nothing comes out, check that printer.");
                    var watch = Stopwatch.StartNew();
                    int hr;
                    try
                    {
                        hr = NativeMethods.StartXpsPrintJob(request.PrinterName, jobName, null, IntPtr.Zero,
                            completion.SafeWaitHandle.DangerousGetHandle(), null, 0, out job, out documentStream, out printTicketStream);
                    }
                    catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
                    {
                        throw new XpsPrintStartException("The XPS Print API is not available on this Windows (" + ex.GetType().Name + ").", ex);
                    }
                    if (hr != 0)
                    {
                        Exception reason = Marshal.GetExceptionForHR(hr);
                        throw new XpsPrintStartException("Windows refused to start a print job on \"" + request.PrinterName + "\": "
                                                         + (reason == null ? "HRESULT 0x" + hr.ToString("X8") : reason.Message), reason);
                    }
                    Log.Info(jobId, "XPS Print API opened a job on \"" + request.PrinterName + "\" as \"" + jobName + "\".");

                    long total = CopyDocument(request.FilePath, documentStream);
                    documentStream.Close();
                    Log.Info(jobId, string.Format(CultureInfo.InvariantCulture,
                        "Document handed to the Windows spooler ({0:N0} bytes in {1} ms); waiting for Windows to finish it.", total, watch.ElapsedMilliseconds));

                    return WaitForCompletion(job, completion, request, onProgress);
                }
                finally
                {
                    Release(printTicketStream);
                    Release(documentStream);
                    Release(job);
                }
            }
        }

        private static long CopyDocument(string path, IXpsPrintJobStream documentStream)
        {
            var buffer = new byte[CopyBufferSize];
            long total = 0;
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int read;
                while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
                {
                    uint written;
                    documentStream.Write(buffer, (uint)read, out written);
                    if (written != read)
                    {
                        throw new IOException("The spooler took " + written + " of " + read + " bytes.");
                    }
                    total += read;
                }
            }
            return total;
        }

        private static PrintOutcome WaitForCompletion(IXpsPrintJob job, ManualResetEvent completion, PrintRequest request, Action<string, string> onProgress)
        {
            string jobId = request.JobId;
            string host = Environment.MachineName;
            DateTime deadline = DateTime.UtcNow + CompletionWait;
            DateTime? signalledAt = null;
            int lastPage = -1;
            uint windowsJobId = 0;

            while (true)
            {
                bool signalled = completion.WaitOne(PollInterval);
                if (signalled && signalledAt == null)
                {
                    signalledAt = DateTime.UtcNow;
                }

                XPS_JOB_STATUS status;
                job.GetJobStatus(out status);
                windowsJobId = status.jobId;

                if (status.currentPage != lastPage && status.currentPage > 0)
                {
                    lastPage = status.currentPage;
                    string page = "page " + status.currentPage + (status.currentPageTotal > 0 ? " of " + status.currentPageTotal : "");
                    Log.Info(jobId, "Windows job " + status.jobId + ": printing " + page + ".");
                    onProgress(JobStates.Printing, "Printing " + page + " on " + request.FriendlyName + " (" + host + ").");
                }

                switch (status.completion)
                {
                    case XPS_JOB_COMPLETION.XPS_JOB_COMPLETED:
                        Log.Info(jobId, "Windows job " + status.jobId + " completed.");
                        return PrintOutcome.Printed("Printed on " + request.FriendlyName + " (" + host + ").");
                    case XPS_JOB_COMPLETION.XPS_JOB_FAILED:
                        string reason = DescribeHResult(status.jobStatus);
                        Log.Warn(jobId, "Windows job " + status.jobId + " failed: " + reason);
                        return PrintOutcome.Error("Windows could not print the job on " + request.FriendlyName + ": " + reason
                                                  + ". Check the printer and its Windows print queue on " + host + ".");
                    case XPS_JOB_COMPLETION.XPS_JOB_CANCELLED:
                        Log.Warn(jobId, "Windows job " + status.jobId + " was cancelled.");
                        return PrintOutcome.Error("The job was cancelled in the Windows print queue on " + host + ".");
                }

                if (signalledAt != null && DateTime.UtcNow - signalledAt.Value > SettleWait)
                {
                    // Windows signalled completion but the status never changed: the job is out of our hands.
                    Log.Info(jobId, "Windows signalled completion for job " + status.jobId + " without a final status; counting it as printed.");
                    return PrintOutcome.Printed("Printed on " + request.FriendlyName + " (" + host + ").");
                }

                if (DateTime.UtcNow > deadline)
                {
                    Log.Warn(jobId, "Windows job " + windowsJobId + " still in progress after " + CompletionWait.TotalMinutes + " minutes; moving on.");
                    return PrintOutcome.StillPrinting("Still in the Windows print queue on " + host + " after "
                                                      + CompletionWait.TotalMinutes.ToString(CultureInfo.InvariantCulture)
                                                      + " minutes (Windows job " + windowsJobId + "). It prints when the printer is ready; check the printer.");
                }
            }
        }

        private static string DescribeHResult(int hresult)
        {
            Exception reason = Marshal.GetExceptionForHR(hresult);
            string text = reason == null ? "" : reason.Message.Trim();
            return (text.Length == 0 ? "error" : text) + " (0x" + hresult.ToString("X8") + ")";
        }

        private static string BuildJobName(PrintRequest request)
        {
            string doc = string.IsNullOrWhiteSpace(request.DocumentName) ? "document" : request.DocumentName.Trim();
            string from = string.IsNullOrWhiteSpace(request.ClientName) ? "" : " from " + request.ClientName.Trim();
            string name = "PrintVect: " + doc + from;
            return name.Length > 120 ? name.Substring(0, 120) : name;
        }

        private static void Release(object comObject)
        {
            if (comObject == null)
            {
                return;
            }
            try
            {
                Marshal.ReleaseComObject(comObject);
            }
            catch (Exception ex)
            {
                Log.Warn("Releasing an XPS Print API object failed: " + ex.Message);
            }
        }

        private static class NativeMethods
        {
            [DllImport("XpsPrint.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
            internal static extern int StartXpsPrintJob(
                string printerName,
                string jobName,
                string outputFileName,
                IntPtr progressEvent,
                IntPtr completionEvent,
                [MarshalAs(UnmanagedType.LPArray)] byte[] printablePagesOn,
                uint printablePagesOnCount,
                out IXpsPrintJob xpsPrintJob,
                out IXpsPrintJobStream documentStream,
                out IXpsPrintJobStream printTicketStream);
        }

        [ComImport, Guid("5ab89b06-8a3d-4c09-92b8-ba5a4caa3cc7"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IXpsPrintJob
        {
            void Cancel();
            void GetJobStatus(out XPS_JOB_STATUS jobStatus);
        }

        [ComImport, Guid("7a77dc5f-45d6-4dff-9307-d8cb846347ca"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IXpsPrintJobStream
        {
            // ISequentialStream
            void Read([MarshalAs(UnmanagedType.LPArray)] byte[] pv, uint cb, out uint pcbRead);
            void Write([MarshalAs(UnmanagedType.LPArray)] byte[] pv, uint cb, out uint pcbWritten);
            // IXpsPrintJobStream
            void Close();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XPS_JOB_STATUS
        {
            public uint jobId;
            public int currentDocument;
            public int currentPage;
            public int currentPageTotal;
            public XPS_JOB_COMPLETION completion;
            public int jobStatus;
        }

        private enum XPS_JOB_COMPLETION
        {
            XPS_JOB_IN_PROGRESS = 0,
            XPS_JOB_COMPLETED = 1,
            XPS_JOB_CANCELLED = 2,
            XPS_JOB_FAILED = 3
        }
    }
}
