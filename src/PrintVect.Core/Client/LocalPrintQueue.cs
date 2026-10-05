using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Client
{
    /// <summary>A job seen in the virtual printer's own Windows queue while the port file was being written.</summary>
    public sealed class LocalQueueJob
    {
        public uint JobId { get; set; }
        /// <summary>What the printing program called the document, e.g. "Untitled - Notepad".</summary>
        public string Document { get; set; }
        public string User { get; set; }
        public DateTime SeenAt { get; set; }
    }

    /// <summary>Reads the jobs of a printer on this PC. Tests replace it with a fake.</summary>
    public interface ILocalPrintQueue
    {
        IList<LocalQueueJob> Jobs(string printerName);
    }

    /// <summary>
    /// EnumJobs on the virtual printer: the XPS Document Writer stores no title in the file it writes,
    /// but while it writes, the job sits in the printer's queue with the document name the user saw.
    /// </summary>
    public sealed class WinspoolPrintQueue : ILocalPrintQueue
    {
        private readonly HashSet<string> _warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _gate = new object();

        public IList<LocalQueueJob> Jobs(string printerName)
        {
            var result = new List<LocalQueueJob>();
            if (string.IsNullOrEmpty(printerName)) return result;

            IntPtr printer = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                if (!NativeMethods.OpenPrinter(printerName, out printer, IntPtr.Zero) || printer == IntPtr.Zero)
                {
                    WarnOnce(printerName, Describe(Marshal.GetLastWin32Error()));
                    return result;
                }
                uint needed, returned;
                NativeMethods.EnumJobs(printer, 0, 0xFFFF, 1, IntPtr.Zero, 0, out needed, out returned);
                if (needed == 0) return result;

                buffer = Marshal.AllocHGlobal((int)needed);
                if (!NativeMethods.EnumJobs(printer, 0, 0xFFFF, 1, buffer, needed, out needed, out returned))
                {
                    WarnOnce(printerName, Describe(Marshal.GetLastWin32Error()));
                    return result;
                }
                int size = Marshal.SizeOf(typeof(JOB_INFO_1));
                for (int i = 0; i < returned; i++)
                {
                    var info = (JOB_INFO_1)Marshal.PtrToStructure(new IntPtr(buffer.ToInt64() + (long)i * size), typeof(JOB_INFO_1));
                    result.Add(new LocalQueueJob
                    {
                        JobId = info.JobId,
                        Document = Marshal.PtrToStringUni(info.pDocument) ?? "",
                        User = Marshal.PtrToStringUni(info.pUserName) ?? "",
                        SeenAt = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                WarnOnce(printerName, ex.Message);
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (printer != IntPtr.Zero) NativeMethods.ClosePrinter(printer);
            }
            return result;
        }

        private void WarnOnce(string printerName, string problem)
        {
            lock (_gate)
            {
                if (!_warned.Add(printerName)) return;
            }
            Log.Warn("The queue of printer \"" + printerName + "\" cannot be read (" + problem + "); jobs will be named after their file instead.");
        }

        private static string Describe(int error)
        {
            return new Win32Exception(error).Message + " (error " + error + ")";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JOB_INFO_1
        {
            public uint JobId;
            public IntPtr pPrinterName;
            public IntPtr pMachineName;
            public IntPtr pUserName;
            public IntPtr pDocument;
            public IntPtr pDatatype;
            public IntPtr pStatus;
            public uint Status;
            public uint Priority;
            public uint Position;
            public uint TotalPages;
            public uint PagesPrinted;
            public SYSTEMTIME Submitted;
        }

        private static class NativeMethods
        {
            [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
            internal static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

            [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
            internal static extern bool ClosePrinter(IntPtr hPrinter);

            [DllImport("winspool.drv", EntryPoint = "EnumJobsW", SetLastError = true, ExactSpelling = true)]
            internal static extern bool EnumJobs(IntPtr hPrinter, uint firstJob, uint noJobs, uint level, IntPtr pJob, uint cbBuf,
                                                 out uint pcbNeeded, out uint pcReturned);
        }
    }
}
