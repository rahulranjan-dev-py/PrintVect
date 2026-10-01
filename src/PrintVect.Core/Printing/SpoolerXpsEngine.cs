using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;

namespace PrintVect.Core.Printing
{
    /// <summary>The spooler refused to open the printer or start the document; nothing was sent, the caller may fall back.</summary>
    public sealed class SpoolerStartException : Exception
    {
        public SpoolerStartException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>
    /// Hands an .xps file straight to the Windows spooler with the plain winspool functions:
    /// StartDocPrinter with the XPS_PASS data type for XPS-based drivers (v4 and XPSDrv) or
    /// XPS2GDI for older GDI drivers (Windows converts in the spooler), WritePrinter, EndDocPrinter,
    /// then GetJob polling. This is the route WPF uses for XPS printers. No COM, no apartments: the
    /// job shows up in the Windows print queue at once and its Windows status is logged.
    /// </summary>
    public sealed class SpoolerXpsEngine : IPrintEngine
    {
        public static readonly TimeSpan StatusWait = TimeSpan.FromMinutes(15);
        /// <summary>A problem (error, paper out, offline, attention) that persists this long ends the job as failed.</summary>
        public static readonly TimeSpan ProblemWait = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        private const int CopyBufferSize = 64 * 1024;
        private const string DatatypeXpsPass = "XPS_PASS";
        private const string DatatypeXps2Gdi = "XPS2GDI";

        public PrintOutcome Print(PrintRequest request, Action<string, string> onProgress, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            string jobId = request.JobId;
            string host = Environment.MachineName;
            string jobName = BuildJobName(request);

            IntPtr printer = IntPtr.Zero;
            try
            {
                if (!NativeMethods.OpenPrinter(request.PrinterName, out printer, IntPtr.Zero) || printer == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new SpoolerStartException("Windows could not open the printer \"" + request.PrinterName + "\": " + Describe(error),
                        new Win32Exception(error));
                }

                DriverInfo driver = ReadDriverInfo(printer, jobId);
                string datatype = driver.IsXpsDriver ? DatatypeXpsPass : DatatypeXps2Gdi;
                onProgress(JobStates.Printing, "Windows is sending the job to " + request.FriendlyName + " on " + host
                                               + ". If nothing comes out, check that printer.");

                var watch = Stopwatch.StartNew();
                uint windowsJobId = StartDocument(printer, jobName, datatype, jobId);
                if (windowsJobId == 0)
                {
                    string other = datatype == DatatypeXpsPass ? DatatypeXps2Gdi : DatatypeXpsPass;
                    Log.Warn(jobId, "The spooler refused data type " + datatype + "; trying " + other + ".");
                    datatype = other;
                    windowsJobId = StartDocument(printer, jobName, datatype, jobId);
                    if (windowsJobId == 0)
                    {
                        int error = Marshal.GetLastWin32Error();
                        throw new SpoolerStartException("Windows could not start a print job on \"" + request.PrinterName + "\" ("
                                                        + datatype + "): " + Describe(error), new Win32Exception(error));
                    }
                }
                Log.Info(jobId, "Spooler job " + windowsJobId + " opened on \"" + request.PrinterName + "\" as \"" + jobName
                                + "\" with data type " + datatype + ".");

                long total = WriteDocument(printer, request.FilePath, jobId);
                Log.Info(jobId, string.Format(CultureInfo.InvariantCulture,
                    "Document handed to the Windows spooler as job {0} ({1:N0} bytes in {2} ms); watching the Windows print queue.",
                    windowsJobId, total, watch.ElapsedMilliseconds));

                return WaitForJob(printer, windowsJobId, request, onProgress);
            }
            finally
            {
                if (printer != IntPtr.Zero && !NativeMethods.ClosePrinter(printer))
                {
                    Log.Warn(jobId, "ClosePrinter failed: " + Describe(Marshal.GetLastWin32Error()));
                }
            }
        }

        private static uint StartDocument(IntPtr printer, string jobName, string datatype, string jobId)
        {
            var info = new DOC_INFO_1 { pDocName = jobName, pOutputFile = null, pDatatype = datatype };
            uint id = NativeMethods.StartDocPrinter(printer, 1, ref info);
            if (id == 0)
            {
                Log.Warn(jobId, "StartDocPrinter(" + datatype + ") failed: " + Describe(Marshal.GetLastWin32Error()));
            }
            return id;
        }

        private static long WriteDocument(IntPtr printer, string path, string jobId)
        {
            bool pageStarted = NativeMethods.StartPagePrinter(printer);
            if (!pageStarted)
            {
                Log.Warn(jobId, "StartPagePrinter failed (" + Describe(Marshal.GetLastWin32Error()) + "); writing without it.");
            }

            long total = 0;
            try
            {
                var buffer = new byte[CopyBufferSize];
                using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int read;
                    while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        uint written;
                        if (!NativeMethods.WritePrinter(printer, buffer, (uint)read, out written))
                        {
                            throw new IOException("WritePrinter failed after " + total + " bytes: " + Describe(Marshal.GetLastWin32Error()));
                        }
                        if (written != read)
                        {
                            throw new IOException("The spooler took " + written + " of " + read + " bytes.");
                        }
                        total += read;
                    }
                }
            }
            finally
            {
                if (pageStarted && !NativeMethods.EndPagePrinter(printer))
                {
                    Log.Warn(jobId, "EndPagePrinter failed: " + Describe(Marshal.GetLastWin32Error()));
                }
                if (!NativeMethods.EndDocPrinter(printer))
                {
                    Log.Warn(jobId, "EndDocPrinter failed: " + Describe(Marshal.GetLastWin32Error()));
                }
            }
            return total;
        }

        private static PrintOutcome WaitForJob(IntPtr printer, uint windowsJobId, PrintRequest request, Action<string, string> onProgress)
        {
            string jobId = request.JobId;
            string host = Environment.MachineName;
            string printedMessage = "Printed on " + request.FriendlyName + " (" + host + ").";
            DateTime deadline = DateTime.UtcNow + StatusWait;
            uint lastFlags = uint.MaxValue;
            string lastText = null;
            int lastPages = -1;
            string lastProblem = null;
            DateTime? problemSince = null;

            while (true)
            {
                JobSnapshot snapshot;
                int error;
                if (!TryGetJob(printer, windowsJobId, out snapshot, out error))
                {
                    // ERROR_INVALID_PARAMETER: the job is no longer in the queue. Finished jobs leave it at once.
                    Log.Info(jobId, "Windows job " + windowsJobId + " is no longer in the print queue (" + Describe(error) + ").");
                    if (lastProblem != null)
                    {
                        return PrintOutcome.Error("The job left the Windows print queue on " + host + " after the printer reported "
                                                  + lastProblem + ". Check the printer.");
                    }
                    return PrintOutcome.Printed(printedMessage);
                }

                if (snapshot.Status != lastFlags || snapshot.StatusText != lastText)
                {
                    lastFlags = snapshot.Status;
                    lastText = snapshot.StatusText;
                    Log.Info(jobId, "Windows job " + windowsJobId + " status: " + DescribeFlags(snapshot.Status)
                                    + (string.IsNullOrEmpty(snapshot.StatusText) ? "" : " \"" + snapshot.StatusText + "\"")
                                    + ", pages " + snapshot.PagesPrinted + "/" + snapshot.TotalPages + ".");
                }

                if (snapshot.PagesPrinted != lastPages && snapshot.PagesPrinted > 0)
                {
                    lastPages = snapshot.PagesPrinted;
                    onProgress(JobStates.Printing, "Printing page " + snapshot.PagesPrinted
                                                   + (snapshot.TotalPages > 0 ? " of " + snapshot.TotalPages : "")
                                                   + " on " + request.FriendlyName + " (" + host + ").");
                }

                if ((snapshot.Status & (JobStatus.Printed | JobStatus.Complete)) != 0)
                {
                    return PrintOutcome.Printed(printedMessage);
                }
                if ((snapshot.Status & JobStatus.Deleted) != 0)
                {
                    return lastProblem == null
                        ? PrintOutcome.Printed(printedMessage)
                        : PrintOutcome.Error("The job was removed from the Windows print queue on " + host + " after the printer reported " + lastProblem + ".");
                }

                string problem = DescribeProblem(snapshot);
                if (problem != lastProblem)
                {
                    lastProblem = problem;
                    problemSince = problem == null ? (DateTime?)null : DateTime.UtcNow;
                    if (problem != null)
                    {
                        Log.Warn(jobId, "Windows reports " + problem + " for job " + windowsJobId + ".");
                        onProgress(JobStates.Printing, "The printer reports " + problem + ". Check " + request.FriendlyName + " on " + host + ".");
                    }
                }
                if (problemSince != null && DateTime.UtcNow - problemSince.Value > ProblemWait)
                {
                    return PrintOutcome.Error("Not printed: Windows reports " + lastProblem + " for the job on " + request.FriendlyName
                                              + ". Fix the printer on " + host + "; the job stays in its Windows print queue (job " + windowsJobId + ").");
                }

                if (DateTime.UtcNow > deadline)
                {
                    return PrintOutcome.StillPrinting("Still in the Windows print queue on " + host + " after "
                                                      + StatusWait.TotalMinutes.ToString(CultureInfo.InvariantCulture)
                                                      + " minutes (Windows job " + windowsJobId + "). It prints when the printer is ready; check the printer.");
                }

                Thread.Sleep(PollInterval);
            }
        }

        private static string DescribeProblem(JobSnapshot snapshot)
        {
            uint s = snapshot.Status;
            string text = string.IsNullOrEmpty(snapshot.StatusText) ? "" : " (" + snapshot.StatusText + ")";
            if ((s & JobStatus.PaperOut) != 0) return "paper out" + text;
            if ((s & JobStatus.Offline) != 0) return "the printer offline" + text;
            if ((s & JobStatus.UserIntervention) != 0) return "attention needed at the printer" + text;
            if ((s & JobStatus.Error) != 0) return "a printing error" + text;
            if ((s & JobStatus.BlockedDevQ) != 0) return "a blocked print queue" + text;
            if ((s & JobStatus.Paused) != 0) return "a paused job" + text;
            return null;
        }

        private static DriverInfo ReadDriverInfo(IntPtr printer, string jobId)
        {
            var result = new DriverInfo();
            uint needed;
            NativeMethods.GetPrinterDriver(printer, null, 8, IntPtr.Zero, 0, out needed);
            if (needed == 0)
            {
                Log.Warn(jobId, "Printer driver details not available (" + Describe(Marshal.GetLastWin32Error()) + "); assuming a GDI driver.");
                return result;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!NativeMethods.GetPrinterDriver(printer, null, 8, buffer, needed, out needed))
                {
                    Log.Warn(jobId, "GetPrinterDriver failed (" + Describe(Marshal.GetLastWin32Error()) + "); assuming a GDI driver.");
                    return result;
                }
                var info = (DRIVER_INFO_8)Marshal.PtrToStructure(buffer, typeof(DRIVER_INFO_8));
                result.Name = Marshal.PtrToStringUni(info.pName) ?? "";
                result.Version = info.cVersion;
                result.Attributes = info.dwPrinterDriverAttributes;
                result.PrintProcessor = Marshal.PtrToStringUni(info.pszPrintProcessor) ?? "";
                result.DefaultDatatype = Marshal.PtrToStringUni(info.pDefaultDataType) ?? "";
                result.IsXpsDriver = info.cVersion >= 4 || (info.dwPrinterDriverAttributes & DriverAttributes.Xps) != 0;
                Log.Info(jobId, "Driver \"" + result.Name + "\": version " + result.Version + ", attributes 0x"
                                + result.Attributes.ToString("X") + (result.IsXpsDriver ? " (XPS-based)" : " (GDI)")
                                + ", print processor \"" + result.PrintProcessor + "\", default data type \"" + result.DefaultDatatype + "\".");
            }
            catch (Exception ex)
            {
                Log.Warn(jobId, "Printer driver details could not be read (" + ex.Message + "); assuming a GDI driver.");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            return result;
        }

        private static bool TryGetJob(IntPtr printer, uint windowsJobId, out JobSnapshot snapshot, out int error)
        {
            snapshot = new JobSnapshot();
            error = 0;
            uint needed;
            NativeMethods.GetJob(printer, windowsJobId, 2, IntPtr.Zero, 0, out needed);
            if (needed == 0)
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!NativeMethods.GetJob(printer, windowsJobId, 2, buffer, needed, out needed))
                {
                    error = Marshal.GetLastWin32Error();
                    return false;
                }
                var info = (JOB_INFO_2)Marshal.PtrToStructure(buffer, typeof(JOB_INFO_2));
                snapshot.Status = info.Status;
                snapshot.StatusText = info.pStatus == IntPtr.Zero ? "" : (Marshal.PtrToStringUni(info.pStatus) ?? "");
                snapshot.PagesPrinted = (int)info.PagesPrinted;
                snapshot.TotalPages = (int)info.TotalPages;
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static string DescribeFlags(uint status)
        {
            if (status == 0) return "queued (0x0)";
            var names = new List<string>();
            foreach (KeyValuePair<uint, string> flag in JobStatus.Names)
            {
                if ((status & flag.Key) != 0) names.Add(flag.Value);
            }
            return string.Join("+", names) + " (0x" + status.ToString("X") + ")";
        }

        private static string Describe(int win32Error)
        {
            return new Win32Exception(win32Error).Message.Trim() + " (error " + win32Error + ")";
        }

        private static string BuildJobName(PrintRequest request)
        {
            string doc = string.IsNullOrWhiteSpace(request.DocumentName) ? "document" : request.DocumentName.Trim();
            string from = string.IsNullOrWhiteSpace(request.ClientName) ? "" : " from " + request.ClientName.Trim();
            string name = "PrintVect: " + doc + from;
            return name.Length > 120 ? name.Substring(0, 120) : name;
        }

        private sealed class DriverInfo
        {
            public string Name = "";
            public uint Version;
            public uint Attributes;
            public string PrintProcessor = "";
            public string DefaultDatatype = "";
            public bool IsXpsDriver;
        }

        private struct JobSnapshot
        {
            public uint Status;
            public string StatusText;
            public int PagesPrinted;
            public int TotalPages;
        }

        private static class JobStatus
        {
            public const uint Paused = 0x1;
            public const uint Error = 0x2;
            public const uint Deleting = 0x4;
            public const uint Spooling = 0x8;
            public const uint Printing = 0x10;
            public const uint Offline = 0x20;
            public const uint PaperOut = 0x40;
            public const uint Printed = 0x80;
            public const uint Deleted = 0x100;
            public const uint BlockedDevQ = 0x200;
            public const uint UserIntervention = 0x400;
            public const uint Restart = 0x800;
            public const uint Complete = 0x1000;
            public const uint Retained = 0x2000;
            public const uint RenderingLocally = 0x4000;

            public static readonly KeyValuePair<uint, string>[] Names =
            {
                new KeyValuePair<uint, string>(Paused, "paused"),
                new KeyValuePair<uint, string>(Error, "error"),
                new KeyValuePair<uint, string>(Deleting, "deleting"),
                new KeyValuePair<uint, string>(Spooling, "spooling"),
                new KeyValuePair<uint, string>(Printing, "printing"),
                new KeyValuePair<uint, string>(Offline, "offline"),
                new KeyValuePair<uint, string>(PaperOut, "paper out"),
                new KeyValuePair<uint, string>(Printed, "printed"),
                new KeyValuePair<uint, string>(Deleted, "deleted"),
                new KeyValuePair<uint, string>(BlockedDevQ, "blocked"),
                new KeyValuePair<uint, string>(UserIntervention, "user intervention"),
                new KeyValuePair<uint, string>(Restart, "restart"),
                new KeyValuePair<uint, string>(Complete, "complete"),
                new KeyValuePair<uint, string>(Retained, "retained"),
                new KeyValuePair<uint, string>(RenderingLocally, "rendering locally")
            };
        }

        private static class DriverAttributes
        {
            public const uint Xps = 0x2;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DOC_INFO_1
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string pDocName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pOutputFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string pDatatype;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOB_INFO_2
        {
            public uint JobId;
            public IntPtr pPrinterName;
            public IntPtr pMachineName;
            public IntPtr pUserName;
            public IntPtr pDocument;
            public IntPtr pNotifyName;
            public IntPtr pDatatype;
            public IntPtr pPrintProcessor;
            public IntPtr pParameters;
            public IntPtr pDriverName;
            public IntPtr pDevMode;
            public IntPtr pStatus;
            public IntPtr pSecurityDescriptor;
            public uint Status;
            public uint Priority;
            public uint Position;
            public uint StartTime;
            public uint UntilTime;
            public uint TotalPages;
            public uint Size;
            public SYSTEMTIME Submitted;
            public uint time;
            public uint PagesPrinted;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DRIVER_INFO_8
        {
            public uint cVersion;
            public IntPtr pName;
            public IntPtr pEnvironment;
            public IntPtr pDriverPath;
            public IntPtr pDataFile;
            public IntPtr pConfigFile;
            public IntPtr pHelpFile;
            public IntPtr pDependentFiles;
            public IntPtr pMonitorName;
            public IntPtr pDefaultDataType;
            public IntPtr pszzPreviousNames;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftDriverDate;
            public ulong dwlDriverVersion;
            public IntPtr pszMfgName;
            public IntPtr pszOEMUrl;
            public IntPtr pszHardwareID;
            public IntPtr pszProvider;
            public IntPtr pszPrintProcessor;
            public IntPtr pszVendorSetup;
            public IntPtr pszzColorProfiles;
            public IntPtr pszInfPath;
            public uint dwPrinterDriverAttributes;
            public IntPtr pszzCoreDriverDependencies;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftMinInboxDriverVerDate;
            public ulong dwlMinInboxDriverVerVersion;
        }

        private static class NativeMethods
        {
            [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
            internal static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

            [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
            internal static extern bool ClosePrinter(IntPtr hPrinter);

            [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
            internal static extern uint StartDocPrinter(IntPtr hPrinter, uint level, ref DOC_INFO_1 pDocInfo);

            [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
            internal static extern bool StartPagePrinter(IntPtr hPrinter);

            [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
            internal static extern bool WritePrinter(IntPtr hPrinter, byte[] pBuf, uint cbBuf, out uint pcWritten);

            [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
            internal static extern bool EndPagePrinter(IntPtr hPrinter);

            [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
            internal static extern bool EndDocPrinter(IntPtr hPrinter);

            [DllImport("winspool.drv", EntryPoint = "GetJobW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
            internal static extern bool GetJob(IntPtr hPrinter, uint jobId, uint level, IntPtr pJob, uint cbBuf, out uint pcbNeeded);

            [DllImport("winspool.drv", EntryPoint = "GetPrinterDriverW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
            internal static extern bool GetPrinterDriver(IntPtr hPrinter, string pEnvironment, uint level, IntPtr pDriverInfo, uint cbBuf, out uint pcbNeeded);
        }
    }
}
