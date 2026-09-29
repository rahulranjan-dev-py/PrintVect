using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using PrintVect.App.Forms;
using PrintVect.Core.Config;
using PrintVect.Core.Logging;

namespace PrintVect.App.Tray
{
    /// <summary>
    /// Owns the tray icon and the main window. Closing the window only hides it; Exit lives in the
    /// tray menu (brief, section 7). The message loop ends when ExitApplication is called.
    /// </summary>
    internal sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly NotifyIcon _trayIcon;
        private readonly MainForm _form;
        private readonly SynchronizationContext _ui;
        private readonly EventWaitHandle _showEvent;
        private readonly RegisteredWaitHandle _showWait;
        private bool _exiting;
        private bool _hideHintShown;

        public TrayApplicationContext(AppPaths paths, ConfigStore store, AppConfig config, bool startHidden)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (store == null) throw new ArgumentNullException(nameof(store));
            if (config == null) throw new ArgumentNullException(nameof(config));

            _form = new MainForm(paths, store, config);
            _form.FormClosing += OnFormClosing;

            // Creating the first control installs the WinForms synchronization context.
            _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

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
            if (!_form.IsDisposed)
            {
                _form.Close();
            }
            ExitThread();
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
                _trayIcon.ShowBalloonTip(5000, Strings.TrayStillRunningTitle, Strings.TrayStillRunningText, ToolTipIcon.Info);
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
                if (_form != null && !_form.IsDisposed)
                {
                    _form.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
