using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using PrintVect.Core.Config;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Elevation
{
    /// <summary>
    /// Runs PrintVect.Elevate.exe with ShellExecute "runas" (Windows shows the UAC prompt), waits for
    /// it, and reads the JSON result file it leaves in the spool folder (brief 11.4).
    /// </summary>
    public static class ElevateLauncher
    {
        public const string ExeName = "PrintVect.Elevate.exe";
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(5);
        private const int ErrorCancelled = 1223;

        public static string DefaultExePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ExeName);
        }

        public static Task<ElevateResult> RunAsync(AppPaths paths, ElevateArguments arguments)
        {
            return RunAsync(DefaultExePath(), paths, arguments, DefaultTimeout);
        }

        public static Task<ElevateResult> RunAsync(string exePath, AppPaths paths, ElevateArguments arguments, TimeSpan timeout)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            return Task.Run(() => Run(exePath, paths, arguments, timeout));
        }

        private static ElevateResult Run(string exePath, AppPaths paths, ElevateArguments arguments, TimeSpan timeout)
        {
            string command = arguments.Command ?? "";
            if (!File.Exists(exePath))
            {
                return Fail(command, ExeName + " is missing from " + Path.GetDirectoryName(exePath)
                                     + ". Copy the whole PrintVect folder again, or run the installer.");
            }

            string resultFile = Path.Combine(paths.SpoolDir, "elevate-" + Guid.NewGuid().ToString("N") + ".json");
            arguments.Options[ElevateCommands.OptionResult] = resultFile;
            string commandLine = arguments.ToCommandLine();
            Log.Info("Asking Windows for permission to run " + ExeName + " " + commandLine);

            var info = new ProcessStartInfo(exePath, commandLine)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
            };

            Process process;
            try
            {
                process = Process.Start(info);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                Log.Warn(ExeName + " was not started: the permission prompt was cancelled.");
                return Fail(command, "Windows asked for permission to change the printers and the request was cancelled. Try again and choose Yes.");
            }
            catch (Exception ex)
            {
                Log.Error(ExeName + " could not be started.", ex);
                return Fail(command, ExeName + " could not be started: " + ex.Message);
            }

            try
            {
                using (process)
                {
                    if (process == null)
                    {
                        return Fail(command, ExeName + " did not start.");
                    }
                    if (!process.WaitForExit((int)timeout.TotalMilliseconds))
                    {
                        Log.Warn(ExeName + " did not finish within " + timeout.TotalSeconds + " s.");
                        return Fail(command, ExeName + " did not finish within " + timeout.TotalMinutes + " minutes. Look at its log in " + paths.LogsDir + ".");
                    }
                    int exitCode = process.ExitCode;
                    Log.Info(ExeName + " finished with exit code " + exitCode + ".");
                    return ReadResult(command, resultFile, exitCode, paths);
                }
            }
            finally
            {
                try { if (File.Exists(resultFile)) File.Delete(resultFile); }
                catch (Exception ex) { Log.Warn("Could not delete " + resultFile + ": " + ex.Message); }
            }
        }

        private static ElevateResult ReadResult(string command, string resultFile, int exitCode, AppPaths paths)
        {
            if (!File.Exists(resultFile))
            {
                return Fail(command, ExeName + " finished with code " + exitCode + " but left no result. Look at its log in " + paths.LogsDir + ".", exitCode);
            }
            try
            {
                ElevateResult result = ElevateResult.FromJson(File.ReadAllText(resultFile));
                if (result == null)
                {
                    return Fail(command, ExeName + " left an empty result (exit code " + exitCode + ").", exitCode);
                }
                Log.Info(ExeName + " result: ok=" + result.Ok + ", " + result.Message);
                return result;
            }
            catch (Exception ex)
            {
                Log.Error("The result file of " + ExeName + " could not be read.", ex);
                return Fail(command, ExeName + " finished with code " + exitCode + " but its result could not be read: " + ex.Message, exitCode);
            }
        }

        private static ElevateResult Fail(string command, string message, int exitCode = ElevateExitCodes.Failed)
        {
            return new ElevateResult { Command = command, Ok = false, ExitCode = exitCode, Message = message, Timestamp = DateTime.Now };
        }
    }
}
