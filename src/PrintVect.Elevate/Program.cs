using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using PrintVect.Core;
using PrintVect.Core.Client;
using PrintVect.Core.Config;
using PrintVect.Core.Diagnostics;
using PrintVect.Core.Elevation;
using PrintVect.Core.Logging;

namespace PrintVect.Elevate
{
    /// <summary>
    /// Command line: PrintVect.Elevate.exe &lt;command&gt; [/option value]... [/result &lt;file&gt;]
    /// Commands: ping, version,
    ///   add-printer /id &lt;printerId&gt; /name "&lt;printer&gt;" /folder "&lt;spool folder&gt;" /port "&lt;port file&gt;" [/driver "&lt;name&gt;"]
    ///   remove-printer /name "&lt;printer&gt;" [/port "&lt;port file&gt;"] [/folder "&lt;spool folder&gt;"]
    ///   remove-all
    /// Exit codes: 0 ok, 1 failed, 2 usage error, 3 not running as administrator.
    /// The outcome is also written as JSON to the result file (default: spool\elevate-result.json).
    /// </summary>
    internal static class Program
    {
        private const string LogPrefix = "PrintVect-Elevate";
        private static AppPaths _paths;

        private static int Main(string[] args)
        {
            _paths = AppPaths.Default();
            try
            {
                _paths.EnsureDirectories();
                Log.Initialize(_paths.LogsDir, LogPrefix);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PrintVect.Elevate could not create " + _paths.Root + ": " + ex.Message);
                return ElevateExitCodes.Failed;
            }

            Log.Info(string.Format("PrintVect.Elevate {0} started. Command line: {1}", AppInfo.Version,
                args.Length == 0 ? "(none)" : string.Join(" ", args)));

            ElevateArguments arguments = ElevateArguments.Parse(args);
            string resultFile = arguments.Get(ElevateCommands.OptionResult) ?? ElevateResult.DefaultFile(_paths);

            ElevateResult result = Run(arguments);
            WriteResult(resultFile, result);

            Console.WriteLine(result.Message);
            Log.Info("PrintVect.Elevate finished: " + result.Command + " -> exit code " + result.ExitCode + " (" + result.Message + ")");
            Log.Shutdown();
            return result.ExitCode;
        }

        private static ElevateResult Run(ElevateArguments arguments)
        {
            string command = arguments.Command ?? "";
            var result = new ElevateResult { Command = command, Timestamp = DateTime.Now };

            if (command.Length == 0)
            {
                return Usage(result, "No command given.");
            }

            if (command == ElevateCommands.Version)
            {
                return Ok(result, AppInfo.ProductName + ".Elevate " + AppInfo.Version);
            }

            if (!IsAdministrator())
            {
                result.ExitCode = ElevateExitCodes.NotElevated;
                result.Message = "PrintVect.Elevate must run as administrator. Right-click it and choose \"Run as administrator\", or let PrintVect start it.";
                return result;
            }

            try
            {
                switch (command)
                {
                    case ElevateCommands.Ping:
                        return Ok(result, "PrintVect.Elevate is working and running as administrator on " + WindowsInfo.Edition() + ".");
                    case ElevateCommands.AddPrinter:
                        return AddPrinter(result, arguments);
                    case ElevateCommands.RemovePrinter:
                        return RemovePrinter(result, arguments);
                    case ElevateCommands.RemoveAll:
                        return RemoveAll(result);
                    default:
                        return Usage(result, "Unknown command \"" + command + "\".");
                }
            }
            catch (Exception ex)
            {
                Log.Error("Command " + command + " failed.", ex);
                result.ExitCode = ElevateExitCodes.Failed;
                result.Message = ex.Message;
                return result;
            }
        }

        private static ElevateResult AddPrinter(ElevateResult result, ElevateArguments arguments)
        {
            string id = arguments.Get(ElevateCommands.OptionId);
            string name = arguments.Get(ElevateCommands.OptionName);
            string folder = arguments.Get(ElevateCommands.OptionFolder);
            string port = arguments.Get(ElevateCommands.OptionPort);
            string requestedDriver = arguments.Get(ElevateCommands.OptionDriver);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(port))
            {
                return Usage(result, "add-printer needs /id, /name, /folder and /port.");
            }
            if (!IsUnderSpool(folder) || !IsUnderSpool(port))
            {
                return Usage(result, "add-printer only creates folders and ports inside " + _paths.SpoolDir + ".");
            }

            IList<string> installed = VirtualPrinters.InstalledDrivers();
            Log.Info("Installed printer drivers: " + (installed.Count == 0 ? "(none)" : string.Join("; ", installed)));
            string driver = VirtualPrinters.ChooseDriver(installed, requestedDriver);
            if (driver == null)
            {
                result.ExitCode = ElevateExitCodes.Failed;
                result.Message = requestedDriver != null
                    ? "The printer driver \"" + requestedDriver + "\" is not installed on this PC."
                    : "Neither \"" + VirtualPrinters.DriverV3 + "\" nor \"" + VirtualPrinters.DriverV4 + "\" is installed on this PC. "
                      + "Turn on the Windows feature \"Microsoft XPS Document Writer\" (Settings, Apps, Optional features, More Windows features) and try again.";
                return result;
            }

            VirtualPrinters.GrantUsersModify(folder);
            VirtualPrinters.AddLocalPort(port);

            bool existed = VirtualPrinters.PrinterExists(name);
            if (existed)
            {
                Log.Info("Printer \"" + name + "\" already exists; keeping it.");
            }
            else
            {
                try
                {
                    VirtualPrinters.AddPrinter(name, port, driver);
                }
                catch (Exception)
                {
                    TryDeletePort(port);
                    throw;
                }
            }

            return Ok(result, (existed ? "The printer \"" + name + "\" was already there." : "Printer \"" + name + "\" created with driver \"" + driver + "\".")
                              + " It writes to " + port + ".");
        }

        private static ElevateResult RemovePrinter(ElevateResult result, ElevateArguments arguments)
        {
            string name = arguments.Get(ElevateCommands.OptionName);
            string port = arguments.Get(ElevateCommands.OptionPort);
            string folder = arguments.Get(ElevateCommands.OptionFolder);
            if (string.IsNullOrWhiteSpace(name))
            {
                return Usage(result, "remove-printer needs /name.");
            }
            if (!ClientPrinterNames.IsPrintVectPrinter(name))
            {
                return Usage(result, "remove-printer only removes printers whose name starts with \"" + ClientPrinterNames.PrinterPrefix + "\".");
            }

            if (string.IsNullOrWhiteSpace(port))
            {
                LocalPrinterEntry entry = VirtualPrinters.LocalPrinters().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                port = entry == null ? null : entry.Port;
            }

            var steps = new List<string>();
            steps.Add(VirtualPrinters.DeletePrinter(name) ? "printer removed" : "printer was already gone");
            if (!string.IsNullOrWhiteSpace(port) && IsUnderSpool(port))
            {
                VirtualPrinters.DeleteLocalPort(port);
                steps.Add("port removed");
            }
            if (!string.IsNullOrWhiteSpace(folder) && IsUnderSpool(folder))
            {
                steps.Add(DeleteFolder(folder) ? "spool folder deleted" : "spool folder could not be deleted (see the log)");
            }
            return Ok(result, "\"" + name + "\": " + string.Join(", ", steps) + ".");
        }

        private static ElevateResult RemoveAll(ElevateResult result)
        {
            int printers = 0;
            var problems = new List<string>();
            foreach (LocalPrinterEntry entry in VirtualPrinters.LocalPrinters().Where(p => ClientPrinterNames.IsPrintVectPrinter(p.Name)))
            {
                try
                {
                    VirtualPrinters.DeletePrinter(entry.Name);
                    printers++;
                    if (IsUnderSpool(entry.Port))
                    {
                        TryDeletePort(entry.Port);
                        DeleteFolder(Path.GetDirectoryName(entry.Port));
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Could not remove \"" + entry.Name + "\".", ex);
                    problems.Add(entry.Name + ": " + ex.Message);
                }
            }
            if (problems.Count > 0)
            {
                result.ExitCode = ElevateExitCodes.Failed;
                result.Message = printers + " PrintVect printer(s) removed; failed: " + string.Join("; ", problems);
                return result;
            }
            return Ok(result, printers + " PrintVect printer(s) removed.");
        }

        private static bool IsUnderSpool(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                string full = Path.GetFullPath(path).TrimEnd('\\');
                string spool = Path.GetFullPath(_paths.SpoolDir).TrimEnd('\\');
                return full.StartsWith(spool + "\\", StringComparison.OrdinalIgnoreCase) && full.Length > spool.Length + 1;
            }
            catch (Exception ex)
            {
                Log.Warn("Path \"" + path + "\" could not be checked: " + ex.Message);
                return false;
            }
        }

        private static void TryDeletePort(string port)
        {
            try
            {
                VirtualPrinters.DeleteLocalPort(port);
            }
            catch (Exception ex)
            {
                Log.Warn("Port \"" + port + "\" could not be removed: " + ex.Message);
            }
        }

        private static bool DeleteFolder(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                    Log.Info("Deleted " + folder);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Folder " + folder + " could not be deleted: " + ex.Message);
                return false;
            }
        }

        private static ElevateResult Ok(ElevateResult result, string message)
        {
            result.Ok = true;
            result.ExitCode = ElevateExitCodes.Ok;
            result.Message = message;
            return result;
        }

        private static ElevateResult Usage(ElevateResult result, string problem)
        {
            result.ExitCode = ElevateExitCodes.UsageError;
            result.Message = problem + " Usage: PrintVect.Elevate.exe <ping|version|add-printer|remove-printer|remove-all> [/option value]... [/result <file>]";
            return result;
        }

        private static void WriteResult(string resultFile, ElevateResult result)
        {
            try
            {
                string folder = Path.GetDirectoryName(resultFile);
                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                File.WriteAllText(resultFile, result.ToJson());
            }
            catch (Exception ex)
            {
                Log.Error("Could not write the result file " + resultFile, ex);
            }
        }

        private static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not determine whether this process is elevated: " + ex.Message);
                return false;
            }
        }
    }
}
