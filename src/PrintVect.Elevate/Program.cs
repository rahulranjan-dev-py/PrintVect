using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using PrintVect.Core;
using PrintVect.Core.Config;
using PrintVect.Core.Diagnostics;
using PrintVect.Core.Elevation;
using PrintVect.Core.Logging;

namespace PrintVect.Elevate
{
    /// <summary>
    /// Command line: PrintVect.Elevate.exe &lt;command&gt; [/result &lt;file&gt;]
    /// Commands in this milestone: ping, version. Printer, port and firewall commands arrive in M2 and M5.
    /// Exit codes: 0 ok, 1 failed, 2 usage error, 3 not running as administrator.
    /// The outcome is also written as JSON to the result file (default: spool\elevate-result.json).
    /// </summary>
    internal static class Program
    {
        private const string LogPrefix = "PrintVect-Elevate";

        private static int Main(string[] args)
        {
            AppPaths paths = AppPaths.Default();
            try
            {
                paths.EnsureDirectories();
                Log.Initialize(paths.LogsDir, LogPrefix);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("PrintVect.Elevate could not create " + paths.Root + ": " + ex.Message);
                return ElevateExitCodes.Failed;
            }

            Log.Info(string.Format("PrintVect.Elevate {0} started. Command line: {1}", AppInfo.Version,
                args.Length == 0 ? "(none)" : string.Join(" ", args)));

            string command;
            string resultFile;
            List<string> extra;
            ParseArguments(args, out command, out resultFile, out extra);
            if (resultFile == null)
            {
                resultFile = ElevateResult.DefaultFile(paths);
            }

            ElevateResult result = Run(command, extra);
            WriteResult(resultFile, result);

            Console.WriteLine(result.Message);
            Log.Info("PrintVect.Elevate finished: " + result.Command + " -> exit code " + result.ExitCode + " (" + result.Message + ")");
            Log.Shutdown();
            return result.ExitCode;
        }

        private static ElevateResult Run(string command, List<string> extra)
        {
            var result = new ElevateResult { Command = command ?? "", Timestamp = DateTime.Now };

            if (string.IsNullOrEmpty(command))
            {
                result.ExitCode = ElevateExitCodes.UsageError;
                result.Message = "Usage: PrintVect.Elevate.exe <ping|version> [/result <file>]";
                return result;
            }

            if (command == "version")
            {
                result.Ok = true;
                result.ExitCode = ElevateExitCodes.Ok;
                result.Message = AppInfo.ProductName + ".Elevate " + AppInfo.Version;
                return result;
            }

            if (!IsAdministrator())
            {
                result.ExitCode = ElevateExitCodes.NotElevated;
                result.Message = "PrintVect.Elevate must run as administrator. Right-click it and choose \"Run as administrator\", or let PrintVect start it.";
                return result;
            }

            switch (command)
            {
                case "ping":
                    result.Ok = true;
                    result.ExitCode = ElevateExitCodes.Ok;
                    result.Message = "PrintVect.Elevate is working and running as administrator on " + WindowsInfo.Edition() + ".";
                    return result;

                default:
                    result.ExitCode = ElevateExitCodes.UsageError;
                    result.Message = "Unknown command \"" + command + "\". Known commands: ping, version.";
                    return result;
            }
        }

        private static void ParseArguments(string[] args, out string command, out string resultFile, out List<string> extra)
        {
            command = null;
            resultFile = null;
            extra = new List<string>();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string bare = arg.TrimStart('/', '-').ToLowerInvariant();

                if (bare == "result" && i + 1 < args.Length)
                {
                    resultFile = args[++i];
                }
                else if (command == null)
                {
                    command = bare;
                }
                else
                {
                    extra.Add(arg);
                }
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

        private static void WriteResult(string resultFile, ElevateResult result)
        {
            try
            {
                string directory = Path.GetDirectoryName(resultFile);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(resultFile, result.ToJson());
                Log.Info("Result written to " + resultFile);
            }
            catch (Exception ex)
            {
                Log.Error("Could not write the result file " + resultFile, ex);
            }
        }
    }
}
