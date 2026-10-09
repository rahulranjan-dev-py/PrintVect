using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PrintVect.Core;
using PrintVect.Core.Config;
using PrintVect.Core.Logging;
using PrintVect.Core.Update;
using Timer = System.Windows.Forms.Timer;

namespace PrintVect.App.Update
{
    internal enum UpdatePhase
    {
        Idle,
        Checking,
        Downloading,
        Installing
    }

    /// <summary>
    /// Drives the update check from the UI thread: one look shortly after start-up and then every
    /// 24 hours while PrintVect runs (when the setting is on), plus "Check now" and "Update now"
    /// from the Settings tab. Every change of state raises Changed on the UI thread.
    /// </summary>
    internal sealed class UpdateController : IDisposable
    {
        public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(45);
        public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

        private readonly AppPaths _paths;
        private readonly ConfigStore _store;
        private readonly AppConfig _config;
        private readonly UpdateChecker _checker;
        private readonly Func<int> _activeJobs;
        private readonly Action _beforeInstall;
        private readonly Timer _timer;
        private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
        private Version _announced;
        private bool _disposed;

        /// <param name="activeJobs">How many print jobs are being handled right now; the update waits for them.</param>
        /// <param name="beforeInstall">Writes everything worth keeping to disk; the setup program ends this process without asking.</param>
        public UpdateController(AppPaths paths, ConfigStore store, AppConfig config, Func<int> activeJobs, Action beforeInstall)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (config == null) throw new ArgumentNullException(nameof(config));
            _paths = paths;
            _store = store;
            _config = config;
            _activeJobs = activeJobs;
            _beforeInstall = beforeInstall;
            _checker = new UpdateChecker(UpdateChecker.DefaultFeedUrl, AppVersions.Current);
            _timer = new Timer();
            _timer.Tick += OnTimer;
        }

        public UpdatePhase Phase { get; private set; }

        public UpdateCheckResult LastResult { get; private set; }

        /// <summary>The newer release found by the last check, or null.</summary>
        public ReleaseInfo Available
        {
            get { return LastResult != null && LastResult.IsNewer ? LastResult.Release : null; }
        }

        /// <summary>Why the last Update now did not finish, in plain language; null when it did or was never pressed.</summary>
        public string LastInstallError { get; private set; }

        public DownloadProgress Progress { get; private set; }

        public DateTime? NextCheckAt { get; private set; }

        public bool AutoCheck
        {
            get { return _config.CheckForUpdates; }
        }

        public string FeedUrl
        {
            get { return _checker.FeedUrl; }
        }

        public event EventHandler Changed;

        /// <summary>A newer version was found; raised once per version per run (for the balloon).</summary>
        public event EventHandler<ReleaseInfo> UpdateFound;

        public void Start()
        {
            UpdateInstaller.CleanUp(_paths.UpdatesDir, AppVersions.Current);
            if (_config.CheckForUpdates)
            {
                Schedule(FirstCheckDelay);
                Log.Info("Update check: first look in " + FirstCheckDelay.TotalSeconds + " s, then every " + CheckInterval.TotalHours + " h.");
            }
            else
            {
                Log.Info("Update check: off in Settings; only Check now looks.");
            }
        }

        public void SetAutoCheck(bool on)
        {
            if (_config.CheckForUpdates == on)
            {
                return;
            }
            _config.CheckForUpdates = on;
            Save();
            Log.Info("Update check: turned " + (on ? "ON (once a day)" : "OFF") + " in Settings.");
            if (on)
            {
                Schedule(TimeSpan.FromSeconds(1));
            }
            else
            {
                _timer.Stop();
                NextCheckAt = null;
            }
            RaiseChanged();
        }

        public Task CheckNowAsync()
        {
            return CheckAsync();
        }

        /// <summary>
        /// Downloads the available release and starts its setup program. Returns null when the
        /// setup started (this process ends shortly after), otherwise the plain-language reason.
        /// </summary>
        public async Task<string> InstallAsync()
        {
            ReleaseInfo release = Available;
            if (release == null || Phase != UpdatePhase.Idle || _disposed)
            {
                return null;
            }

            int busy = 0;
            try
            {
                busy = _activeJobs == null ? 0 : _activeJobs();
            }
            catch (Exception ex)
            {
                Log.Warn("Update: the active jobs could not be counted: " + ex.Message);
            }
            if (busy > 0)
            {
                Log.Info("Update: not started, " + busy + " job(s) still being handled.");
                return string.Format(Strings.UpdateJobsBusy, busy);
            }

            LastInstallError = null;
            Progress = null;
            Phase = UpdatePhase.Downloading;
            RaiseChanged();

            string path;
            try
            {
                var progress = new Progress<DownloadProgress>(p =>
                {
                    Progress = p;
                    RaiseChanged();
                });
                path = await _checker.DownloadAsync(release, _paths.UpdatesDir, progress, _stopping.Token);
            }
            catch (OperationCanceledException)
            {
                Phase = UpdatePhase.Idle;
                RaiseChanged();
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("Update: the download of " + release.Installer.Name + " failed.", ex);
                LastInstallError = string.Format(Strings.UpdateDownloadFailed, UpdateChecker.DescribeError(ex));
                Phase = UpdatePhase.Idle;
                RaiseChanged();
                return LastInstallError;
            }

            try
            {
                if (_beforeInstall != null)
                {
                    _beforeInstall();
                }
            }
            catch (Exception ex)
            {
                Log.Error("Update: could not write everything to disk before the update.", ex);
            }

            Phase = UpdatePhase.Installing;
            RaiseChanged();
            Process process;
            try
            {
                process = UpdateInstaller.Start(path);
            }
            catch (Exception ex)
            {
                Log.Error("Update: the setup program could not be started.", ex);
                LastInstallError = string.Format(Strings.UpdateStartFailed, ex.Message);
                Phase = UpdatePhase.Idle;
                RaiseChanged();
                return LastInstallError;
            }
            if (process == null)
            {
                Log.Warn("Update: Windows started the setup program but gave no handle to wait for; this program keeps running until the setup ends it.");
                return null;
            }
            WatchSetup(process, path);
            return null;
        }

        public IEnumerable<string> DescribeForDiagnostics()
        {
            var lines = new List<string>();
            lines.Add("This PC runs " + AppInfo.Version + "; daily check " + (AutoCheck
                ? "ON" + (NextCheckAt.HasValue ? ", next at " + NextCheckAt.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "")
                : "OFF") + ", phase now: " + Phase + ".");
            UpdateCheckResult result = LastResult;
            if (result == null)
            {
                lines.Add("Last check: none yet in this run.");
            }
            else
            {
                string when = result.CheckedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                switch (result.Outcome)
                {
                    case UpdateCheckOutcome.UpdateAvailable:
                        lines.Add("Last check " + when + ": " + AppVersions.Format(result.Release.Version) + " is available ("
                                  + result.Release.Installer.Name + ", " + result.Release.Installer.Size + " bytes"
                                  + (result.Release.InstallerHash == null ? ", NO checksum file" : ", with checksum file") + ").");
                        break;
                    case UpdateCheckOutcome.Failed:
                        lines.Add("Last check " + when + ": FAILED, " + result.Error + ".");
                        break;
                    default:
                        lines.Add("Last check " + when + ": up to date" + (result.Release == null
                            ? " (no release published yet)" : " (latest release " + result.Release.Tag + ")") + ".");
                        break;
                }
            }
            if (LastInstallError != null)
            {
                lines.Add("Last update attempt: " + LastInstallError);
            }
            lines.Add("Release feed: " + FeedUrl);
            string folder = _paths.UpdatesDir;
            string contents;
            try
            {
                contents = Directory.Exists(folder)
                    ? "(" + (Directory.GetFiles(folder).Length == 0 ? "empty" : string.Join(", ", Directory.GetFiles(folder).Select(Path.GetFileName))) + ")"
                    : "(not created yet)";
            }
            catch (Exception ex)
            {
                contents = "(not readable: " + ex.Message + ")";
            }
            lines.Add("Updates folder: " + folder + " " + contents);
            return lines;
        }

        private void Schedule(TimeSpan delay)
        {
            if (_disposed)
            {
                return;
            }
            _timer.Stop();
            _timer.Interval = (int)Math.Min(int.MaxValue - 1, Math.Max(1000.0, delay.TotalMilliseconds));
            NextCheckAt = DateTime.Now + delay;
            _timer.Start();
        }

        private async void OnTimer(object sender, EventArgs e)
        {
            _timer.Stop();
            NextCheckAt = null;
            if (_disposed || !_config.CheckForUpdates)
            {
                return;
            }
            await CheckAsync();
            if (!_disposed && _config.CheckForUpdates)
            {
                Schedule(CheckInterval);
            }
        }

        private async Task CheckAsync()
        {
            if (Phase != UpdatePhase.Idle || _disposed)
            {
                return;
            }
            Phase = UpdatePhase.Checking;
            RaiseChanged();
            try
            {
                UpdateCheckResult result = await _checker.CheckAsync(_stopping.Token);
                LastResult = result;
                if (result.IsNewer && _announced != result.Release.Version)
                {
                    _announced = result.Release.Version;
                    Raise(UpdateFound, result.Release);
                }
            }
            catch (OperationCanceledException)
            {
                // PrintVect is closing.
            }
            catch (Exception ex)
            {
                Log.Error("Update check: unexpected failure.", ex);
                LastResult = new UpdateCheckResult
                {
                    Outcome = UpdateCheckOutcome.Failed,
                    Error = ex.Message,
                    CheckedAt = DateTime.Now,
                    Current = AppVersions.Current
                };
            }
            finally
            {
                if (Phase == UpdatePhase.Checking)
                {
                    Phase = UpdatePhase.Idle;
                }
                RaiseChanged();
            }
        }

        private async void WatchSetup(Process process, string path)
        {
            int code;
            try
            {
                code = await Task.Run(() =>
                {
                    process.WaitForExit();
                    return process.ExitCode;
                });
            }
            catch (Exception ex)
            {
                Log.Warn("Update: could not wait for the setup program: " + ex.Message);
                return;
            }
            finally
            {
                process.Dispose();
            }
            if (_disposed)
            {
                return;
            }
            // This program is still running, so the setup did not replace it: the Windows permission
            // question was answered No, or the setup failed (its log says why).
            Log.Warn("Update: the setup program ended with code " + code + " while PrintVect " + AppInfo.Version
                     + " is still running, so the update did not happen. Setup log: " + UpdateInstaller.LogPathFor(path));
            LastInstallError = string.Format(Strings.UpdateDidNotRun, code);
            Phase = UpdatePhase.Idle;
            RaiseChanged();
        }

        private void Save()
        {
            try
            {
                _store.Save(_config);
            }
            catch (Exception ex)
            {
                Log.Error("Could not save " + _store.Path, ex);
            }
        }

        private void RaiseChanged()
        {
            EventHandler handler = Changed;
            if (handler == null) return;
            try { handler(this, EventArgs.Empty); }
            catch (Exception ex) { Log.Error("A UI update listener failed.", ex); }
        }

        private void Raise(EventHandler<ReleaseInfo> handler, ReleaseInfo release)
        {
            if (handler == null) return;
            try { handler(this, release); }
            catch (Exception ex) { Log.Error("A UI update listener failed.", ex); }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _timer.Stop();
            _timer.Dispose();
            _stopping.Cancel();
        }
    }
}
