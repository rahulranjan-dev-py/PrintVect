using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using PrintVect.Core.Logging;

namespace PrintVect.Elevate
{
    /// <summary>A printer installed on this PC, as seen by the spooler.</summary>
    internal sealed class LocalPrinterEntry
    {
        public string Name;
        public string Port;
        public string Driver;
    }

    /// <summary>
    /// The admin-only spooler calls (brief 5.1 and 11.4): Local Port add/delete through XcvData,
    /// AddPrinter with the built-in XPS Document Writer driver, DeletePrinter, driver and printer
    /// lists, and the folder permission that lets the standard-user app take the spooled files.
    /// </summary>
    internal static class VirtualPrinters
    {
        public const string DriverV3 = "Microsoft XPS Document Writer";
        public const string DriverV4 = "Microsoft XPS Document Writer v4";
        private const string LocalPortMonitor = ",XcvMonitor Local Port";
        private const uint ServerAccessAdminister = 0x00000001;
        private const uint PrinterAllAccess = 0x000F000C;
        private const uint PrinterAttributeQueued = 0x00000001;
        private const uint PrinterAttributeLocal = 0x00000040;
        private const uint PrinterControlPurge = 3;
        private const uint PrinterEnumLocal = 0x00000002;
        private const int ErrorFileNotFound = 2;
        private const int ErrorInsufficientBuffer = 122;
        private const int ErrorAlreadyExists = 183;
        private const int ErrorUnknownPort = 1796;
        private const int ErrorInvalidPrinterName = 1801;

        public static IList<string> InstalledDrivers()
        {
            uint needed, returned;
            NativeMethods.EnumPrinterDrivers(null, null, 2, IntPtr.Zero, 0, out needed, out returned);
            if (needed == 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 0 && error != ErrorInsufficientBuffer) throw new Win32Exception(error, "The printer driver list could not be read: " + Describe(error));
                return new List<string>();
            }
            IntPtr buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!NativeMethods.EnumPrinterDrivers(null, null, 2, buffer, needed, out needed, out returned))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "The printer driver list could not be read: " + Describe(error));
                }
                var names = new List<string>();
                int size = Marshal.SizeOf(typeof(DRIVER_INFO_2));
                for (int i = 0; i < returned; i++)
                {
                    var info = (DRIVER_INFO_2)Marshal.PtrToStructure(new IntPtr(buffer.ToInt64() + (long)i * size), typeof(DRIVER_INFO_2));
                    string name = Marshal.PtrToStringUni(info.pName);
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
                }
                return names;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// The driver for the virtual printer: the one asked for, else the Windows 7 style "Microsoft XPS
        /// Document Writer" (writes .xps, which every host prints), else the v4 writer (writes .oxps).
        /// Null when neither is installed.
        /// </summary>
        public static string ChooseDriver(IList<string> installed, string requested)
        {
            if (installed == null) throw new ArgumentNullException(nameof(installed));
            if (!string.IsNullOrWhiteSpace(requested))
            {
                return installed.FirstOrDefault(d => string.Equals(d, requested.Trim(), StringComparison.OrdinalIgnoreCase));
            }
            return installed.FirstOrDefault(d => string.Equals(d, DriverV3, StringComparison.OrdinalIgnoreCase))
                   ?? installed.FirstOrDefault(d => string.Equals(d, DriverV4, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Installs one of Windows' own printer drivers from the driver store (what PowerShell's
        /// Add-PrinterDriver does without an .inf path). Returns null when done, else the problem.
        /// </summary>
        public static string TryInstallDriverFromStore(string driverName)
        {
            try
            {
                int hr = NativeMethods.InstallPrinterDriverFromPackage(null, null, driverName, null, 0);
                if (hr == 0)
                {
                    Log.Info("Printer driver \"" + driverName + "\" installed from the Windows driver store.");
                    return null;
                }
                Exception reason = Marshal.GetExceptionForHR(hr);
                string problem = "0x" + hr.ToString("X8") + (reason == null ? "" : " " + reason.Message);
                Log.Warn("Printer driver \"" + driverName + "\" could not be installed from the driver store: " + problem);
                return problem;
            }
            catch (Exception ex)
            {
                Log.Warn("Printer driver \"" + driverName + "\" could not be installed from the driver store: " + ex.Message);
                return ex.Message;
            }
        }

        /// <summary>
        /// Turns on the Windows feature "Microsoft XPS Document Writer" with DISM (inbox, no internet).
        /// Returns null when the feature is on, else the problem. Sets restartNeeded when Windows says so.
        /// </summary>
        public static string TryEnableXpsWriterFeature(TimeSpan timeout, out bool restartNeeded)
        {
            restartNeeded = false;
            string dism = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "dism.exe");
            if (!File.Exists(dism))
            {
                return dism + " was not found.";
            }
            var info = new ProcessStartInfo(dism, "/online /enable-feature /featurename:Printing-XPSServices-Features /all /norestart")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            Log.Info("Turning on the Windows feature Microsoft XPS Document Writer: " + dism + " " + info.Arguments);
            try
            {
                using (Process process = Process.Start(info))
                {
                    if (process == null) return "DISM did not start.";
                    var output = new System.Text.StringBuilder();
                    process.OutputDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (output) output.AppendLine(e.Data); };
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                    {
                        try { process.Kill(); } catch (Exception ex) { Log.Warn("DISM could not be stopped: " + ex.Message); }
                        return "DISM did not finish within " + timeout.TotalMinutes + " minutes.";
                    }
                    process.WaitForExit();
                    string text;
                    lock (output) text = output.ToString().Trim();
                    Log.Info("DISM finished with exit code " + process.ExitCode + (text.Length == 0 ? "." : ":\n" + text));
                    if (process.ExitCode == 0) return null;
                    if (process.ExitCode == 3010)
                    {
                        restartNeeded = true;
                        return null;
                    }
                    return "DISM exit code " + process.ExitCode + (text.Length == 0 ? "" : " (" + LastLine(text) + ")");
                }
            }
            catch (Exception ex)
            {
                Log.Warn("DISM could not be run: " + ex.Message);
                return ex.Message;
            }
        }

        private static string LastLine(string text)
        {
            string[] lines = text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? "" : lines[lines.Length - 1].Trim();
        }

        public static void AddLocalPort(string portName)
        {
            Xcv("AddPort", portName, ErrorAlreadyExists);
        }

        public static void DeleteLocalPort(string portName)
        {
            Xcv("DeletePort", portName, ErrorUnknownPort, ErrorFileNotFound);
        }

        private static void Xcv(string command, string portName, params int[] acceptableStatuses)
        {
            if (string.IsNullOrWhiteSpace(portName)) throw new ArgumentException("A port name is required.", nameof(portName));
            var defaults = new PRINTER_DEFAULTS { pDatatype = IntPtr.Zero, pDevMode = IntPtr.Zero, DesiredAccess = ServerAccessAdminister };
            IntPtr monitor;
            if (!NativeMethods.OpenPrinter(LocalPortMonitor, out monitor, ref defaults) || monitor == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, "The Local Port monitor could not be opened: " + Describe(error));
            }
            try
            {
                byte[] data = Encoding.Unicode.GetBytes(portName + "\0");
                uint needed, status;
                if (!NativeMethods.XcvData(monitor, command, data, (uint)data.Length, IntPtr.Zero, 0, out needed, out status))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, command + " for port \"" + portName + "\" failed: " + Describe(error));
                }
                if (status != 0 && Array.IndexOf(acceptableStatuses, (int)status) < 0)
                {
                    throw new Win32Exception((int)status, command + " for port \"" + portName + "\" failed: " + Describe((int)status));
                }
                Log.Info(command + " \"" + portName + "\": " + (status == 0 ? "done" : "nothing to do (" + Describe((int)status) + ")"));
            }
            finally
            {
                NativeMethods.ClosePrinter(monitor);
            }
        }

        public static bool PrinterExists(string name)
        {
            IntPtr printer;
            if (NativeMethods.OpenPrinter(name, out printer, IntPtr.Zero) && printer != IntPtr.Zero)
            {
                NativeMethods.ClosePrinter(printer);
                return true;
            }
            return false;
        }

        public static void AddPrinter(string name, string portName, string driverName)
        {
            var info = new PRINTER_INFO_2_W
            {
                pServerName = null,
                pPrinterName = name,
                pShareName = null,
                pPortName = portName,
                pDriverName = driverName,
                pComment = "Prints on another PC through PrintVect.",
                pLocation = "",
                pDevMode = IntPtr.Zero,
                pSepFile = null,
                pPrintProcessor = "winprint",
                pDatatype = "RAW",
                pParameters = null,
                pSecurityDescriptor = IntPtr.Zero,
                Attributes = PrinterAttributeLocal | PrinterAttributeQueued,
                Priority = 1,
                DefaultPriority = 1
            };
            IntPtr printer = NativeMethods.AddPrinter(null, 2, ref info);
            if (printer == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new Win32Exception(error, "Windows could not create the printer \"" + name + "\" with driver \"" + driverName + "\": " + Describe(error));
            }
            NativeMethods.ClosePrinter(printer);
            Log.Info("Printer \"" + name + "\" created on port \"" + portName + "\" with driver \"" + driverName + "\".");
        }

        /// <summary>Deletes the printer (after purging its queue). A printer that is already gone is not an error.</summary>
        public static bool DeletePrinter(string name)
        {
            var defaults = new PRINTER_DEFAULTS { pDatatype = IntPtr.Zero, pDevMode = IntPtr.Zero, DesiredAccess = PrinterAllAccess };
            IntPtr printer;
            if (!NativeMethods.OpenPrinter(name, out printer, ref defaults) || printer == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ErrorInvalidPrinterName)
                {
                    Log.Info("Printer \"" + name + "\" does not exist; nothing to delete.");
                    return false;
                }
                throw new Win32Exception(error, "The printer \"" + name + "\" could not be opened for deletion: " + Describe(error));
            }
            try
            {
                if (!NativeMethods.SetPrinter(printer, 0, IntPtr.Zero, PrinterControlPurge))
                {
                    Log.Warn("Could not purge the queue of \"" + name + "\" (" + Describe(Marshal.GetLastWin32Error()) + "); deleting anyway.");
                }
                if (!NativeMethods.DeletePrinter(printer))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "Windows could not delete the printer \"" + name + "\": " + Describe(error));
                }
                Log.Info("Printer \"" + name + "\" deleted.");
                return true;
            }
            finally
            {
                NativeMethods.ClosePrinter(printer);
            }
        }

        public static IList<LocalPrinterEntry> LocalPrinters()
        {
            uint needed, returned;
            NativeMethods.EnumPrinters(PrinterEnumLocal, null, 2, IntPtr.Zero, 0, out needed, out returned);
            if (needed == 0)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != 0 && error != ErrorInsufficientBuffer) throw new Win32Exception(error, "The printer list could not be read: " + Describe(error));
                return new List<LocalPrinterEntry>();
            }
            IntPtr buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!NativeMethods.EnumPrinters(PrinterEnumLocal, null, 2, buffer, needed, out needed, out returned))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "The printer list could not be read: " + Describe(error));
                }
                var result = new List<LocalPrinterEntry>();
                int size = Marshal.SizeOf(typeof(PRINTER_INFO_2));
                for (int i = 0; i < returned; i++)
                {
                    var info = (PRINTER_INFO_2)Marshal.PtrToStructure(new IntPtr(buffer.ToInt64() + (long)i * size), typeof(PRINTER_INFO_2));
                    result.Add(new LocalPrinterEntry
                    {
                        Name = Marshal.PtrToStringUni(info.pPrinterName) ?? "",
                        Port = Marshal.PtrToStringUni(info.pPortName) ?? "",
                        Driver = Marshal.PtrToStringUni(info.pDriverName) ?? ""
                    });
                }
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>
        /// Lets every user of the PC rename and delete the files the spooler writes into the folder
        /// (ProgramData gives Users only create rights; files there belong to whoever created them).
        /// </summary>
        public static void GrantUsersModify(string folder)
        {
            Directory.CreateDirectory(folder);
            DirectorySecurity security = Directory.GetAccessControl(folder);
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            security.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.Modify | FileSystemRights.Synchronize,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.SetAccessControl(folder, security);
            Log.Info("Users may now modify " + folder + " and the files in it.");
        }

        private static string Describe(int error)
        {
            return new Win32Exception(error).Message + " (error " + error + ")";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PRINTER_DEFAULTS
        {
            public IntPtr pDatatype;
            public IntPtr pDevMode;
            public uint DesiredAccess;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DRIVER_INFO_2
        {
            public uint cVersion;
            public IntPtr pName;
            public IntPtr pEnvironment;
            public IntPtr pDriverPath;
            public IntPtr pDataFile;
            public IntPtr pConfigFile;
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

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PRINTER_INFO_2_W
        {
            [MarshalAs(UnmanagedType.LPWStr)] public string pServerName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pPrinterName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pShareName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pPortName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pDriverName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pComment;
            [MarshalAs(UnmanagedType.LPWStr)] public string pLocation;
            public IntPtr pDevMode;
            [MarshalAs(UnmanagedType.LPWStr)] public string pSepFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string pPrintProcessor;
            [MarshalAs(UnmanagedType.LPWStr)] public string pDatatype;
            [MarshalAs(UnmanagedType.LPWStr)] public string pParameters;
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

            [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
            internal static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, ref PRINTER_DEFAULTS pDefault);

            [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
            internal static extern bool ClosePrinter(IntPtr hPrinter);

            [DllImport("winspool.drv", EntryPoint = "XcvDataW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
            internal static extern bool XcvData(IntPtr hXcv, string pszDataName, byte[] pInputData, uint cbInputData,
                                                IntPtr pOutputData, uint cbOutputData, out uint pcbOutputNeeded, out uint pdwStatus);

            [DllImport("winspool.drv", EntryPoint = "AddPrinterW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
            internal static extern IntPtr AddPrinter(string pName, uint level, ref PRINTER_INFO_2_W pPrinter);

            [DllImport("winspool.drv", SetLastError = true, ExactSpelling = true)]
            internal static extern bool DeletePrinter(IntPtr hPrinter);

            [DllImport("winspool.drv", EntryPoint = "SetPrinterW", SetLastError = true, ExactSpelling = true)]
            internal static extern bool SetPrinter(IntPtr hPrinter, uint level, IntPtr pPrinter, uint command);

            [DllImport("winspool.drv", EntryPoint = "EnumPrinterDriversW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
            internal static extern bool EnumPrinterDrivers(string pName, string pEnvironment, uint level, IntPtr pDriverInfo, uint cbBuf,
                                                           out uint pcbNeeded, out uint pcReturned);

            [DllImport("winspool.drv", EntryPoint = "InstallPrinterDriverFromPackageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
            internal static extern int InstallPrinterDriverFromPackage(string pszServer, string pszInfPath, string pszDriverName,
                                                                      string pszEnvironment, uint dwFlags);

            [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
            internal static extern bool EnumPrinters(uint flags, string name, uint level, IntPtr pPrinterEnum, uint cbBuf,
                                                     out uint pcbNeeded, out uint pcReturned);
        }
    }
}
