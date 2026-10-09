using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using PrintVect.App.Client;
using PrintVect.App.Forms;
using PrintVect.App.Host;
using PrintVect.App.Update;
using PrintVect.Core;
using PrintVect.Core.Client;
using PrintVect.Core.Config;
using PrintVect.Core.Host;
using PrintVect.Core.Logging;
using PrintVect.Core.Protocol;
using PrintVect.Core.Update;

namespace PrintVect.App.Tray
{
    /// <summary>
    /// Owns the tray icon, the host controller and the main window. Closing the window only hides
    /// it; Exit lives in the tray menu (brief, section 7). Balloons announce received, printed
    /// and failed jobs on both sides, and (M6a) a newer PrintVect. The message loop ends when
    /// ExitApplication is called.
    /// </summary>
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private const int BalloonMilliseconds = 5000;
        private const int MaxBalloonText = 240;

        private readonly NotifyIcon _trayIcon;
        private readonly HostController _host;
        private readonly ClientController _client;
        private readonly UpdateController _updates;
        private readonly MainForm _form;
        private readonly SynchronizationContext _ui;
        private readonly EventWaitHandle _showEvent;
        private readonly RegisteredWaitHandle _showWait;
        private bool _exiting;
        private bool _hideHintShown;
        private bool _lastBalloonAboutUpdate;

        public TrayApplicationContext(AppPaths paths, ConfigStore store, AppConfig config, bool startHidden)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (config == null) throw new ArgumentNullException(nameof(config));

            // Make sure background threads can post to this (the UI) thread before anything else exists.
            SynchronizationContext current = SynchronizationContext.Current;
            if (!(current is WindowsFormsSynchronizationContext))
            {
                current = new WindowsFormsSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(current);
            }
            _ui = current;

            _host = new HostController(paths, store, config, _ui);
            _host.JobReceived += OnJobReceived;
            _host.JobFinished += OnJobFinished;

            _client = new ClientController(paths, store, config, _ui);
            _client.JobFinished += OnClientJobFinished;

            _updates = new UpdateController(paths, store, config,
                () => _host.ActiveJobCount + _client.ActiveJobCount,
                () =>
                {
                    _host.FlushHistory();
                    _client.FlushHistory();
                });
            _updates.UpdateFound += OnUpdateFound;

            _form = new MainForm(paths, store, config, _host, _client, _updates);
            _form.FormClosing += OnFormClosing;

            var menu = new ContextMenuStrip();
            var open = new ToolStripMenuItem(Strings.TrayOpen, null, (s, e) => ShowWindow());
            open.Font = new Font(menu.Font, FontStyle.Bold);
            menu.Items.Add(open);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem(Strings.TrayExit, null, (s, e) => ExitApplication()));

            _trayIcon = new NotifyIcon
            {
                Icon = AppIcon.Tray,
                Text = Strings.AppName,
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ShowWindow();
                }
            };
            _trayIcon.BalloonTipClicked += (s, e) =>
            {
                if (_lastBalloonAboutUpdate)
                {
                    ShowWindow();
                    _form.ShowSettings();
                }
            };

            _showEvent = SingleInstance.CreateShowEvent();
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
                (state, timedOut) => _ui.Post(_ => ShowWindow(), null), null, Timeout.Infinite, false);

            SystemEvents.SessionEnding += OnSessionEnding;

            if (startHidden)
            {
                Log.Info("Started hidden in the tray (" + Program.TraySwitch + ").");
            }
            else
            {
                ShowWindow();
            }

            _host.StartIfConfigured();
            if (_host.LastError != null)
            {
                Balloon(Strings.BalloonSharingFailedTitle, _host.LastError, ToolTipIcon.Warning);
            }
            _client.StartIfConfigured();
            if (_client.LastError != null)
            {
                Balloon(Strings.BalloonClientStartFailedTitle, _client.LastError, ToolTipIcon.Warning);
            }

            NoteVersionChange(store, config);
            _updates.Start();
        }

        /// <summary>The first start after an update says so (the setup program restarted PrintVect quietly).</summary>
        private void NoteVersionChange(ConfigStore store, AppConfig config)
        {
            string last = config.LastRunVersion ?? "";
            if (last == AppInfo.Version)
            {
                return;
            }
            if (last.Length > 0)
            {
                Log.Info("PrintVect was updated from " + last + " to " + AppInfo.Version + ".");
                Balloon(Strings.BalloonUpdatedTitle, string.Format(Strings.BalloonUpdatedText, AppInfo.Version), ToolTipIcon.Info);
            }
            config.LastRunVersion = AppInfo.Version;
            try
            {
                store.Save(config);
            }
            catch (Exception ex)
            {
                Log.Error("Could not save " + store.Path + " after noting the program version.", ex);
            }
        }

        private void OnUpdateFound(object sender, ReleaseInfo release)
        {
            Balloon(Strings.BalloonUpdateTitle, string.Format(Strings.BalloonUpdateText, AppVersions.Format(release.Version)),
                ToolTipIcon.Info, true);
        }

        public void ShowWindow()
        {
            if (_exiting || _form.IsDisposed)
            {
                return;
            }
            if (!_form.Visible)
            {
                _form.Show();
            }
            if (_form.WindowState == FormWindowState.Minimized)
            {
                _form.WindowState = FormWindowState.Normal;
            }
            _form.Activate();
            _form.BringToFront();
        }

        public void ExitApplication()
        {
            if (_exiting)
            {
                return;
            }
            _exiting = true;
            Log.Info("Exit chosen; closing the tray icon and the window.");
            _trayIcon.Visible = false;
            _updates.Dispose();
            _client.Dispose();
            _host.Dispose();
            if (!_form.IsDisposed)
            {
                _form.Close();
            }
            ExitThread();
        }

        private void OnJobReceived(object sender, JobRecord record)
        {
            Balloon(Strings.AppName, string.Format(Strings.BalloonJobReceived, record.Doc, record.Client, record.PrinterFriendly), ToolTipIcon.Info);
        }

        private void OnJobFinished(object sender, JobRecord record)
        {
            if (record.State == JobStates.Printed)
            {
                Balloon(Strings.AppName, string.Format(Strings.BalloonJobPrinted, record.Doc, record.Client, record.PrinterFriendly), ToolTipIcon.Info);
            }
            else if (record.State == JobStates.Error)
            {
                Balloon(Strings.AppName, string.Format(Strings.BalloonJobFailed, record.Doc, record.Client, record.Message), ToolTipIcon.Warning);
            }
        }

        private void OnClientJobFinished(object sender, ClientJobRecord record)
        {
            if (record.State == ClientJobStates.Printed)
            {
                Balloon(Strings.AppName, string.Format(Strings.BalloonClientPrinted, record.Doc, record.HostName), ToolTipIcon.Info);
            }
            else if (record.State == ClientJobStates.Error)
            {
                Balloon(Strings.AppName, string.Format(Strings.BalloonClientFailed, record.Doc, record.Message), ToolTipIcon.Warning);
            }
            else if (record.State == ClientJobStates.Pending)
            {
                Balloon(Strings.AppName, string.Format(Strings.BalloonClientPending, record.Doc, record.Message), ToolTipIcon.Warning);
            }
        }

        private void Balloon(string title, string text, ToolTipIcon icon, bool aboutUpdate = false)
        {
            if (_exiting || string.IsNullOrEmpty(text))
            {
                return;
            }
            _lastBalloonAboutUpdate = aboutUpdate;
            if (text.Length > MaxBalloonText)
            {
                text = text.Substring(0, MaxBalloonText - 3) + "...";
            }
            try
            {
                _trayIcon.ShowBalloonTip(BalloonMilliseconds, title, text, icon);
            }
            catch (Exception ex)
            {
                Log.Warn("Balloon could not be shown: " + ex.Message);
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_exiting || e.CloseReason != CloseReason.UserClosing)
            {
                return;
            }

            // The X button hides the window; PrintVect keeps working in the tray.
            e.Cancel = true;
            _form.Hide();
            if (!_hideHintShown)
            {
                _hideHintShown = true;
                Balloon(Strings.TrayStillRunningTitle, Strings.TrayStillRunningText, ToolTipIcon.Info);
            }
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            Log.Info("Windows is ending the session (" + e.Reason + "); PrintVect is closing.");
            ExitApplication();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.SessionEnding -= OnSessionEnding;
                if (_showWait != null)
                {
                    _showWait.Unregister(null);
                }
                if (_showEvent != null)
                {
                    _showEvent.Dispose();
                }
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
                if (_updates != null)
                {
                    _updates.Dispose();
                }
                if (_client != null)
                {
                    _client.Dispose();
                }
                if (_host != null)
                {
                    _host.Dispose();
                }
                if (_form != null && !_form.IsDisposed)
                {
                    _form.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
