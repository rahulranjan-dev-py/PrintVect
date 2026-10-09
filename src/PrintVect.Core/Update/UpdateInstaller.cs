using System;
using System.Diagnostics;
using System.IO;
using PrintVect.Core.Logging;

namespace PrintVect.Core.Update
{
    /// <summary>
    /// Runs the downloaded setup program and keeps the updates folder tidy. The setup is started
    /// like a double-click (no "runas"): Inno Setup asks Windows for administrator rights itself,
    /// and only then does its "Start PrintVect now" step run as the signed-in user again instead
    /// of as the administrator. /SILENT shows just the progress bar. The setup ends the running
    /// PrintVect itself (taskkill in its PrepareToInstall step) and starts the new one when done.
    /// </summary>
    public static class UpdateInstaller
    {
        public const string LogPrefix = "update-";
        public const string LogExtension = ".log";
        public const string SilentArguments = "/SILENT /NORESTART";

        public static string BuildArguments(string logPath)
        {
            return string.IsNullOrEmpty(logPath) ? SilentArguments : SilentArguments + " /LOG=\"" + logPath + "\"";
        }

        /// <summary>...\updates\PrintVect-Setup-0.2.1.exe gives ...\updates\update-0.2.1.log.</summary>
        public static string LogPathFor(string installerPath)
        {
            string folder = Path.GetDirectoryName(installerPath) ?? "";
            Version version = VersionFromFileName(Path.GetFileName(installerPath));
            string tag = version == null ? Path.GetFileNameWithoutExtension(installerPath) : AppVersions.Format(version);
            return Path.Combine(folder, LogPrefix + tag + LogExtension);
        }

        /// <summary>"PrintVect-Setup-0.2.1.exe" gives 0.2.1; null for any other name.</summary>
        public static Version VersionFromFileName(string fileName)
        {
            if (!ReleaseInfo.IsInstallerName(fileName))
            {
                return null;
            }
            string middle = fileName.Substring(ReleaseInfo.InstallerPrefix.Length,
                fileName.Length - ReleaseInfo.InstallerPrefix.Length - ReleaseInfo.InstallerExtension.Length);
            return AppVersions.Parse(middle);
        }

        public static Process Start(string installerPath)
        {
            if (!File.Exists(installerPath))
            {
                throw new FileNotFoundException("The setup program is missing.", installerPath);
            }
            var info = new ProcessStartInfo(installerPath, BuildArguments(LogPathFor(installerPath)))
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? ""
            };
            Log.Info("Update: starting \"" + installerPath + "\" " + info.Arguments);
            return Process.Start(info);
        }

        /// <summary>
        /// After a start: removes setup programs that are not newer than the running version (the
        /// update happened, or was never needed) and half-downloaded files. Logs stay until the
        /// next download replaces them. Never throws.
        /// </summary>
        public static int CleanUp(string folder, Version current)
        {
            int removed = 0;
            try
            {
                if (!Directory.Exists(folder))
                {
                    return 0;
                }
                foreach (string file in Directory.GetFiles(folder))
                {
                    string name = Path.GetFileName(file);
                    bool stale = name.EndsWith(UpdateChecker.PartExtension, StringComparison.OrdinalIgnoreCase);
                    if (!stale)
                    {
                        Version version = VersionFromFileName(name);
                        stale = version != null && !AppVersions.IsNewer(version, current);
                    }
                    if (!stale)
                    {
                        continue;
                    }
                    try
                    {
                        File.Delete(file);
                        removed++;
                        Log.Info("Update: removed " + file + " (no longer needed).");
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Update: could not remove " + file + ": " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Update: could not tidy " + folder + ": " + ex.Message);
            }
            return removed;
        }
    }
}
