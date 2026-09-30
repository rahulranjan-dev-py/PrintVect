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
    /// <summary>The XPS Print API refused to start the job, or failed before any data was sent; the caller may fall back.</summary>
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
    ///
    /// The job and stream objects are used through their raw COM function tables. On the owner's
    /// Windows 11 PC the objects refused .NET's automatic QueryInterface for IXpsPrintJob
    /// (E_NOINTERFACE) from both STA and MTA threads although the job had been started, so no
    /// interface cast is attempted; the function-table layout is fixed by the API definition.
    /// </summary>
    public sealed class XpsPrintEngine : IPrintEngine
    {
        /// <summary>How long one job may keep its printer's print thread before the thread moves on.</summary>
        public static readonly TimeSpan CompletionWait = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan SettleWait = TimeSpan.FromSeconds(5);
        private const int CopyBufferSize = 64 * 1024;
        private static int _probeLogged;

        public PrintOutcome Print(PrintRequest request, Action<string, string> onProgress, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return ApartmentRunner.Run(ApartmentState.MTA, "PrintVect XPS print: " + request.JobId,
                () => PrintOnMtaThread(request, onProgress));
        }

        private static PrintOutcome PrintOnMtaThread(PrintRequest request, Action<string, string> onProgress)
        {
            string jobId = request.JobId;
            string host = Environment.MachineName;
            string jobName = BuildJobName(request);

            int comInit = NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.COINIT_MULTITHREADED);
            Log.Info(jobId, "COM on the print thread: CoInitializeEx(MTA) returned 0x" + comInit.ToString("X8")
                            + " (0 = initialised now, 1 = already MTA, 0x80010106 = thread is STA).");
            try
            {
                return PrintWithApi(request, onProgress, jobId, host, jobName);
            }
            finally
            {
                if (comInit == 0 || comInit == 1)
                {
                    NativeMethods.CoUninitialize();
                }
            }
        }

        private static PrintOutcome PrintWithApi(PrintRequest request, Action<string, string> onProgress, string jobId, string host, string jobName)
        {
            using (var completion = new ManualResetEvent(false))
            {
                IntPtr jobPtr = IntPtr.Zero;
                IntPtr documentPtr = IntPtr.Zero;
                IntPtr ticketPtr = IntPtr.Zero;
                bool dataSent = false;
                try
                {
                    onProgress(JobStates.Printing, "Windows is sending the job to " + request.FriendlyName + " on " + host
                                                   + ". If nothing comes out, check that printer.");
                    var watch = Stopwatch.StartNew();
                    int hr;
                    try
                    {
                        hr = NativeMethods.StartXpsPrintJob(request.PrinterName, jobName, null, IntPtr.Zero,
                            completion.SafeWaitHandle.DangerousGetHandle(), null, 0, out jobPtr, out documentPtr, out ticketPtr);
                    }
                    catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
                    {
                        throw new XpsPrintStartException("The XPS Print API is not available on this Windows (" + ex.GetType().Name + ").", ex);
                    }
                    if (hr != 0)
                    {
                        throw new XpsPrintStartException("Windows refused to start a print job on \"" + request.PrinterName + "\": "
                                                         + DescribeHResult(hr), Marshal.GetExceptionForHR(hr));
                    }
                    if (jobPtr == IntPtr.Zero || documentPtr == IntPtr.Zero)
                    {
                        throw new XpsPrintStartException("Windows started the print job on \"" + request.PrinterName
                                                         + "\" but returned no job object.", null);
                    }
                    Log.Info(jobId, "XPS Print API opened a job on \"" + request.PrinterName + "\" as \"" + jobName + "\" after "
                                    + watch.ElapsedMilliseconds + " ms.");
                    LogInterfaceProbeOnce(jobId, jobPtr, documentPtr);

                    var job = new XpsJob(jobPtr);
                    var document = new XpsStream(documentPtr);
                    long total;
                    try
                    {
                        total = CopyDocument(request.FilePath, document);
                        dataSent = true;
                        document.Close();
                    }
                    catch (Exception ex) when (!dataSent)
                    {
                        throw new XpsPrintStartException("The document could not be handed to the spooler: " + ex.Message, ex);
                    }
                    Log.Info(jobId, string.Format(CultureInfo.InvariantCulture,
                        "Document handed to the Windows spooler ({0:N0} bytes in {1} ms); waiting for Windows to finish it.", total, watch.ElapsedMilliseconds));

                    return WaitForCompletion(job, completion, request, onProgress);
                }
                finally
                {
                    Release(ticketPtr);
                    Release(documentPtr);
                    Release(jobPtr);
                }
            }
        }

        private static long CopyDocument(string path, XpsStream documentStream)
        {
            var buffer = new byte[CopyBufferSize];
            long total = 0;
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                int read;
                while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
                {
                    uint written = documentStream.Write(buffer, (uint)read);
                    if (written != read)
                    {
                        throw new IOException("The spooler took " + written + " of " + read + " bytes.");
                    }
                    total += read;
                }
            }
            return total;
        }

        private static PrintOutcome WaitForCompletion(XpsJob job, ManualResetEvent completion, PrintRequest request, Action<string, string> onProgress)
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

                XPS_JOB_STATUS status = job.GetStatus();
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

        /// <summary>Logs, once per run, which interface ids the job and stream objects admit to, for the record.</summary>
        private static void LogInterfaceProbeOnce(string jobId, IntPtr jobPtr, IntPtr documentPtr)
        {
            if (Interlocked.Exchange(ref _probeLogged, 1) != 0)
            {
                return;
            }
            try
            {
                Log.Info(jobId, "XPS Print API interface probe: job object -> IXpsPrintJob " + Probe(jobPtr, NativeMethods.IID_IXpsPrintJob)
                                + ", IXpsPrintJobStream " + Probe(jobPtr, NativeMethods.IID_IXpsPrintJobStream)
                                + "; document stream -> IXpsPrintJobStream " + Probe(documentPtr, NativeMethods.IID_IXpsPrintJobStream)
                                + ", ISequentialStream " + Probe(documentPtr, NativeMethods.IID_ISequentialStream) + ".");
            }
            catch (Exception ex)
            {
                Log.Warn(jobId, "Interface probe failed: " + ex.Message);
            }
        }

        private static string Probe(IntPtr unknown, Guid iid)
        {
            IntPtr result;
            int hr = Marshal.QueryInterface(unknown, ref iid, out result);
            if (hr == 0 && result != IntPtr.Zero)
            {
                Marshal.Release(result);
                return "yes";
            }
            return "no (0x" + hr.ToString("X8") + ")";
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

        private static void Release(IntPtr unknown)
        {
            if (unknown == IntPtr.Zero)
            {
                return;
            }
            try
            {
                Marshal.Release(unknown);
            }
            catch (Exception ex)
            {
                Log.Warn("Releasing an XPS Print API object failed: " + ex.Message);
            }
        }

        /// <summary>Calls a method through a COM object's function table: slot 0..2 are IUnknown.</summary>
        private static T Method<T>(IntPtr unknown, int slot) where T : class
        {
            IntPtr vtable = Marshal.ReadIntPtr(unknown);
            IntPtr function = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return Marshal.GetDelegateForFunctionPointer(function, typeof(T)) as T;
        }

        private static void Check(int hr, string what)
        {
            if (hr != 0)
            {
                throw new COMException(what + " failed: " + DescribeHResult(hr), hr);
            }
        }

        /// <summary>IXpsPrintJob: IUnknown + Cancel (slot 3) + GetJobStatus (slot 4).</summary>
        private sealed class XpsJob
        {
            private readonly IntPtr _self;
            private readonly GetJobStatusFn _getJobStatus;

            public XpsJob(IntPtr self)
            {
                _self = self;
                _getJobStatus = Method<GetJobStatusFn>(self, 4);
            }

            public XPS_JOB_STATUS GetStatus()
            {
                XPS_JOB_STATUS status;
                Check(_getJobStatus(_self, out status), "IXpsPrintJob.GetJobStatus");
                return status;
            }
        }

        /// <summary>IXpsPrintJobStream: IUnknown + Read (3) + Write (4) + Close (5).</summary>
        private sealed class XpsStream
        {
            private readonly IntPtr _self;
            private readonly WriteFn _write;
            private readonly CloseFn _close;

            public XpsStream(IntPtr self)
            {
                _self = self;
                _write = Method<WriteFn>(self, 4);
                _close = Method<CloseFn>(self, 5);
            }

            public uint Write(byte[] buffer, uint count)
            {
                uint written;
                Check(_write(_self, buffer, count, out written), "IXpsPrintJobStream.Write");
                return written;
            }

            public void Close()
            {
                Check(_close(_self), "IXpsPrintJobStream.Close");
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetJobStatusFn(IntPtr self, out XPS_JOB_STATUS status);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int WriteFn(IntPtr self, [MarshalAs(UnmanagedType.LPArray)] byte[] buffer, uint count, out uint written);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CloseFn(IntPtr self);

        private static class NativeMethods
        {
            public const int COINIT_MULTITHREADED = 0x0;
            public static readonly Guid IID_IXpsPrintJob = new Guid("5ab89b06-8a3d-4c09-92b8-ba5a4caa3cc7");
            public static readonly Guid IID_IXpsPrintJobStream = new Guid("7a77dc5f-45d6-4dff-9307-d8cb846347ca");
            public static readonly Guid IID_ISequentialStream = new Guid("0c733a30-2a1c-11ce-ade5-00aa0044773d");

            [DllImport("XpsPrint.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
            internal static extern int StartXpsPrintJob(
                string printerName,
                string jobName,
                string outputFileName,
                IntPtr progressEvent,
                IntPtr completionEvent,
                [MarshalAs(UnmanagedType.LPArray)] byte[] printablePagesOn,
                uint printablePagesOnCount,
                out IntPtr xpsPrintJob,
                out IntPtr documentStream,
                out IntPtr printTicketStream);

            [DllImport("ole32.dll", ExactSpelling = true)]
            internal static extern int CoInitializeEx(IntPtr reserved, int coInit);

            [DllImport("ole32.dll", ExactSpelling = true)]
            internal static extern void CoUninitialize();
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
