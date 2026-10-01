using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Printing
{
    /// <summary>
    /// Reads the Windows port a printer sits on (USB001, PORTPROMPT:, an IP port, ...) with the plain
    /// winspool GetPrinter call, and knows the ports that ask the host user for a file name.
    /// </summary>
    public static class PrinterPorts
    {
        /// <summary>Microsoft Print to PDF and the XPS Document Writer: Windows shows a Save window on the host.</summary>
        public const string PromptPort = "PORTPROMPT:";
        /// <summary>The classic "print to file" port; Windows asks for a file name there too.</summary>
        public const string FilePort = "FILE:";

        /// <summary>True for the ports where Windows asks the host user where to save the output.</summary>
        public static bool IsPromptingPort(string portName)
        {
            if (string.IsNullOrEmpty(portName)) return false;
            string port = portName.Trim();
            return port.Equals(PromptPort, StringComparison.OrdinalIgnoreCase)
                   || port.Equals(FilePort, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The printer's port name, or null (logged with the jobId) when Windows could not tell.</summary>
        public static string GetPortName(string printerName, string jobId)
        {
            IntPtr printer = IntPtr.Zero;
            IntPtr buffer = IntPtr.Zero;
            try
            {
                if (!NativeMethods.OpenPrinter(printerName, out printer, IntPtr.Zero) || printer == IntPtr.Zero)
                {
                    Log.Warn(jobId, "Port of printer \"" + printerName + "\" not read: " + Describe(Marshal.GetLastWin32Error()));
                    return null;
                }

                uint needed;
                NativeMethods.GetPrinter(printer, 2, IntPtr.Zero, 0, out needed);
                if (needed == 0)
                {
                    Log.Warn(jobId, "Port of printer \"" + printerName + "\" not read: " + Describe(Marshal.GetLastWin32Error()));
                    return null;
                }

                buffer = Marshal.AllocHGlobal((int)needed);
                if (!NativeMethods.GetPrinter(printer, 2, buffer, needed, out needed))
                {
                    Log.Warn(jobId, "Port of printer \"" + printerName + "\" not read: " + Describe(Marshal.GetLastWin32Error()));
                    return null;
                }

                var info = (PRINTER_INFO_2)Marshal.PtrToStructure(buffer, typeof(PRINTER_INFO_2));
                return Marshal.PtrToStringUni(info.pPortName);
            }
            catch (Exception ex)
            {
                Log.Warn(jobId, "Port of printer \"" + printerName + "\" not read: " + ex.Message);
                return null;
            }
            finally
            {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (printer != IntPtr.Zero) NativeMethods.ClosePrinter(printer);
            }
        }

        private static string Describe(int error)
        {
            return new Win32Exception(error).Message + " (error " + error + ")";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PRINTER_INFO_2
        {
            public IntPtr pServerName;
            public IntPtr pPrinterName;
            public IntPtr pShareName;
            public IntPtr pPortName;
            public IntPtr pDriverName;
            public IntPtr pComment;
            public IntPtr pLocation;
            public IntPtr pDevMode;
            public IntPtr pSepFile;
            public IntPtr pPrintProcessor;
            public IntPtr pDatatype;
            public IntPtr pParameters;
            public IntPtr pSecurityDescriptor;
            public uint Attributes;
            public uint Priority;
            public uint DefaultPriority;
            public uint StartTime;
            public uint UntilTime;
            public uint Status;
            public uint cJobs;
            public uint AveragePPM;
        }

        private static class NativeMethods
        {
            [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
            internal static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

            [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
            internal static extern bool ClosePrinter(IntPtr hPrinter);

            [DllImport("winspool.drv", EntryPoint = "GetPrinterW", SetLastError = true, ExactSpelling = true)]
            internal static extern bool GetPrinter(IntPtr hPrinter, uint level, IntPtr pPrinter, uint cbBuf, out uint pcbNeeded);
        }
    }
}
