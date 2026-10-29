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
    /// Every user may write in the updates folder, so the verified file is held open (Hold) from its last check until
    /// the setup has started (0.5.8): nobody can change, replace or rename it, or a folder above it, in between.
    /// </summary>
    /// <summary>The verified setup program held open (UpdateInstaller.Hold); disposing lets go of it.</summary>
    public sealed class SetupHold : IDisposable
    {
        private readonly FileStream _stream;

        internal SetupHold(string path, FileStream stream)
        {
            Path = path;
            _stream = stream;
        }

        public string Path { get; }

        public void Dispose()
        {
            _stream.Dispose();
        }
    }

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

        /// <summary>
        /// Opens the verified setup program so that no one can write, delete or rename it while the hold lasts, and
        /// checks its size and SHA-256 again through that very handle. Throws UpdateVerificationException when it
        /// changed after the download was checked.
        /// </summary>
        public static SetupHold Hold(VerifiedSetup setup)
        {
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            if (!File.Exists(setup.Path))
            {
                throw new FileNotFoundException("The setup program is missing.", setup.Path);
            }
            var stream = new FileStream(setup.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                string hash = Sha256Hex.OfStream(stream);
                if (stream.Length != setup.Size || !Sha256Hex.Equal(hash, setup.Sha256))
                {
                    Log.Error("Update: " + setup.Path + " changed after it was checked (" + stream.Length + " bytes, SHA-256 " + hash
                              + "; checked: " + setup.Size + " bytes, " + setup.Sha256 + "). It is not started.");
                    throw new UpdateVerificationException("the downloaded setup program was changed on this PC after it was checked");
                }
                Log.Info("Update: " + setup.Path + " checked again just before starting it: the SHA-256 still matches.");
                return new SetupHold(setup.Path, stream);
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        /// <summary>Starts the held setup program; keep the hold until it ends (or this program is ended by it).</summary>
        public static Process Start(SetupHold hold)
        {
            if (hold == null) throw new ArgumentNullException(nameof(hold));
            string installerPath = hold.Path;
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
