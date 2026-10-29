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
    ///   autostart /on yes|no
    ///   network-private /networks &lt;network id&gt;[,&lt;network id&gt;...]
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
                // Every user may write in the data folder: never follow a link planted there with administrator rights (0.5.8).
                string link = SafeFolders.FirstLinkInDataFolders(_paths);
                if (link != null)
                {
                    Console.Error.WriteLine(new LinkFoundException(link).Message);
                    return ElevateExitCodes.Failed;
                }
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
                    case ElevateCommands.Autostart:
                        return SetAutostart(result, arguments);
                    case ElevateCommands.NetworkPrivate:
                        return MakeNetworksPrivate(result, arguments);
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
            var notes = new List<string>();
            bool restartNeeded = false;
            if (driver == null && requestedDriver == null)
            {
                driver = InstallXpsWriterDriver(notes, out restartNeeded);
            }
            if (driver == null)
            {
                result.ExitCode = ElevateExitCodes.Failed;
                result.Message = requestedDriver != null
                    ? "The printer driver \"" + requestedDriver + "\" is not installed on this PC."
                    : "Neither \"" + VirtualPrinters.DriverV3 + "\" nor \"" + VirtualPrinters.DriverV4 + "\" is installed on this PC, and PrintVect could not install it"
                      + (notes.Count == 0 ? "." : " (" + string.Join("; ", notes) + ").")
                      + (restartNeeded ? " Windows asks for a restart: restart this PC, then press Add to this PC again."
                                       : " Turn on the Windows feature \"Microsoft XPS Document Writer\" (Settings, Apps, Optional features, More Windows features), restart if Windows asks, and try again.");
                return result;
            }

            SafeFolders.GrantUsersModify(SafeFolders.TopOf(_paths), folder);   // refuses a junction or link on the way (0.5.8)
            Log.Info("Users may now modify " + folder + " and the files in it.");
            SafeFolders.RefuseLinks(SafeFolders.TopOf(_paths), port);
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
                              + " It writes to " + port + "." + (notes.Count == 0 ? "" : " (" + string.Join("; ", notes) + ")"));
        }

        /// <summary>
        /// No XPS Document Writer driver is installed: install Windows' own one from the driver store,
        /// and when the store does not have it, turn on the Windows feature and try again.
        /// Returns the driver name that is installed afterwards, or null.
        /// </summary>
        private static string InstallXpsWriterDriver(List<string> notes, out bool restartNeeded)
        {
            restartNeeded = false;
            string[] candidates = WindowsInfo.IsWindows8OrLater
                ? new[] { VirtualPrinters.DriverV4, VirtualPrinters.DriverV3 }
                : new[] { VirtualPrinters.DriverV3 };

            string driver = InstallFromStore(candidates, notes);
            if (driver != null) return driver;

            Log.Info("No XPS Document Writer driver in the driver store; turning on the Windows feature.");
            string problem = VirtualPrinters.TryEnableXpsWriterFeature(TimeSpan.FromMinutes(4), out restartNeeded);
            if (problem != null)
            {
                notes.Add("Windows feature: " + problem);
                return null;
            }
            notes.Add("the Windows feature Microsoft XPS Document Writer was turned on" + (restartNeeded ? ", Windows asks for a restart" : ""));

            IList<string> installed = VirtualPrinters.InstalledDrivers();
            Log.Info("Installed printer drivers now: " + (installed.Count == 0 ? "(none)" : string.Join("; ", installed)));
            driver = VirtualPrinters.ChooseDriver(installed, null);
            return driver ?? InstallFromStore(candidates, notes);
        }

        private static string InstallFromStore(string[] candidates, List<string> notes)
        {
            foreach (string candidate in candidates)
            {
                string problem = VirtualPrinters.TryInstallDriverFromStore(candidate);
                if (problem != null)
                {
                    notes.Add("\"" + candidate + "\" from the driver store: " + problem);
                    continue;
                }
                IList<string> installed = VirtualPrinters.InstalledDrivers();
                string driver = VirtualPrinters.ChooseDriver(installed, null);
                if (driver != null)
                {
                    notes.Add("\"" + driver + "\" was installed from the Windows driver store");
                    return driver;
                }
                notes.Add("\"" + candidate + "\" was reported installed but is not listed");
            }
            return null;
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
                SafeFolders.RefuseLinks(SafeFolders.TopOf(_paths), folder);   // never delete where a planted link leads (0.5.8)
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

        /// <summary>Start with Windows on or off for every user: the Run entry PrintVect's installer creates.</summary>
        private static ElevateResult SetAutostart(ElevateResult result, ElevateArguments arguments)
        {
            string value = (arguments.Get(ElevateCommands.OptionOn) ?? "").Trim().ToLowerInvariant();
            if (value != "yes" && value != "no")
            {
                return Usage(result, "autostart needs /on yes or /on no.");
            }
            bool on = value == "yes";
            string app = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PrintVect.App.exe");
            Autostart.Set(on, app);
            Log.Info("Start with Windows " + (on ? "ON: " + Autostart.CommandFor(app) : "OFF") + ".");
            return Ok(result, on ? "PrintVect now starts with Windows (in the tray) for every user of this PC."
                                 : "PrintVect no longer starts with Windows on this PC. Start it from the Start menu when needed.");
        }

        /// <summary>
        /// The hotspot fix (0.5.0): Windows treats a phone hotspot as a Public network, where the firewall rules of the
        /// installer do not let other PCs in. Making the network Private opens it to them, as on the office LAN.
        /// </summary>
        private static ElevateResult MakeNetworksPrivate(ElevateResult result, ElevateArguments arguments)
        {
            var ids = new List<Guid>();
            foreach (string part in (arguments.Get(ElevateCommands.OptionNetworks) ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                Guid id;
                if (!Guid.TryParse(part.Trim(), out id))
                {
                    return Usage(result, "\"" + part.Trim() + "\" is not a network id.");
                }
                ids.Add(id);
            }
            if (ids.Count == 0)
            {
                return Usage(result, "network-private needs /networks with one or more network ids.");
            }

            var done = new List<string>();
            var problems = new List<string>();
            foreach (Guid id in ids)
            {
                try
                {
                    done.Add("\u201c" + NetworkProfiles.SetPrivate(id) + "\u201d");
                }
                catch (Exception ex)
                {
                    Log.Error("Network " + id + " could not be made Private.", ex);
                    problems.Add(ex.Message);
                }
            }
            if (problems.Count > 0)
            {
                result.ExitCode = ElevateExitCodes.Failed;
                result.Message = "Windows did not change the network: " + string.Join(" ", problems)
                                 + " Ask whoever looks after this PC to set it to Private in Windows Settings, Network.";
                return result;
            }
            return Ok(result, string.Join(" and ", done) + (done.Count == 1 ? " is" : " are")
                              + " now a private network. Other PCs on it can reach this PC's printers.");
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
            result.Message = problem + " Usage: PrintVect.Elevate.exe <ping|version|add-printer|remove-printer|remove-all|autostart|network-private> [/option value]... [/result <file>]";
            return result;
        }

        private static void WriteResult(string resultFile, ElevateResult result)
        {
            try
            {
                string folder = Path.GetDirectoryName(resultFile);
                string top = SafeFolders.TopOf(_paths);
                bool inDataFolder = !string.IsNullOrEmpty(folder) && Path.GetFullPath(resultFile).StartsWith(top + "\\", StringComparison.OrdinalIgnoreCase);
                if (inDataFolder)
                {
                    SafeFolders.CreateFolder(top, folder);
                    SafeFolders.RefuseLinks(top, resultFile);
                }
                else if (!string.IsNullOrEmpty(folder))
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
