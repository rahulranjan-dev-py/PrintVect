using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrintVect.App.Client;
using PrintVect.App.Host;
using PrintVect.App.Theme;
using PrintVect.App.Update;
using PrintVect.Core;
using PrintVect.Core.Config;
using PrintVect.Core.Diagnostics;
using PrintVect.Core.Logging;
using PrintVect.Core.Update;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// "Settings" (the design puts Diagnostics here as a section): the current settings, the Updates card (M6a) and
    /// the Diagnostics card that collects the text to send to whoever supports PrintVect.
    /// </summary>
    internal sealed class SettingsTab : UserControl
    {
        private readonly AppPaths _paths;
        private readonly ConfigStore _store;
        private readonly AppConfig _config;
        private readonly HostController _host;
        private readonly ClientController _client;
        private readonly UpdateController _updates;

        private ToggleSwitch _updateAutoCheck;
        private Label _updateStatus;
        private ThinProgress _updateProgress;
        private ThemedButton _checkNowButton;
        private ThemedButton _updateNowButton;
        private TextBox _diagnosticsText;
        private ThemedButton _refreshButton;
        private ThemedButton _copyButton;
        private bool _diagnosticsLoaded;
        private bool _collecting;

        public SettingsTab(AppPaths paths, ConfigStore store, AppConfig config, HostController host, ClientController client,
            UpdateController updates)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (updates == null) throw new ArgumentNullException(nameof(updates));
            _paths = paths;
            _store = store;
            _config = config;
            _host = host;
            _client = client;
            _updates = updates;
            BackColor = Palette.Background;
            BuildLayout();
            RefreshUpdateUi();
            _updates.Changed += OnUpdatesChanged;
        }

        /// <summary>Raised with a short text for the status bar ("Copied ...").</summary>
        public event EventHandler<string> Message;

        private void BuildLayout()
        {
            SuspendLayout();
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Ui.Pad(0), BackColor = Palette.Background };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(400)));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            TableLayoutPanel left = Make.Column(0, 0);
            left.Margin = Ui.Pad(0, 0, 16, 0);
            left.Controls.Add(new SettingsCard(_paths, _store, _config, _host, _client, text => Message?.Invoke(this, text)), 0, 0);
            left.Controls.Add(CreateUpdatesCard(), 0, 1);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Ui.Pad(0), BackColor = Palette.Background };
            left.Dock = DockStyle.Top;
            left.AutoSize = true;
            left.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            scroll.Controls.Add(left);
            NativeTheme.UseDarkScrollBars(scroll);

            layout.Controls.Add(scroll, 0, 0);
            layout.Controls.Add(CreateDiagnosticsCard(), 1, 0);
            Controls.Add(layout);
            ResumeLayout(true);
        }

        private Card CreateUpdatesCard()
        {
            var card = new Card { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Ui.Pad(0) };
            TableLayoutPanel column = Make.Column(0, 0, 0, 0, 0);
            column.AutoSize = true;
            column.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            column.Dock = DockStyle.Top;

            _updateAutoCheck = new ToggleSwitch(Strings.SettingsCheckForUpdates)
            {
                Checked = _updates.AutoCheck,
                WrapWidth = 300,
                Font = ThemeFonts.Text,
                Margin = Ui.Pad(0, 4, 0, 12)
            };
            _updateAutoCheck.CheckedChanged += (s, e) => _updates.SetAutoCheck(_updateAutoCheck.Checked);
            _updateStatus = Make.Muted("");
            _updateProgress = new ThinProgress { Dock = DockStyle.Fill, Visible = false, Margin = Ui.Pad(0, 0, 0, 10) };
            _updateNowButton = new ThemedButton(Strings.SettingsUpdateNow, ButtonKind.Primary) { Visible = false };
            _updateNowButton.Click += (s, e) => StartUpdate();
            _checkNowButton = new ThemedButton(Strings.SettingsCheckNow, ButtonKind.Secondary);
            _checkNowButton.Click += (s, e) => CheckForUpdatesNow();

            column.Controls.Add(Make.Title(Strings.SettingsUpdatesGroup), 0, 0);
            column.Controls.Add(_updateAutoCheck, 0, 1);
            column.Controls.Add(_updateStatus, 0, 2);
            column.Controls.Add(_updateProgress, 0, 3);
            FlowLayoutPanel buttons = Make.Row(_updateNowButton, _checkNowButton);
            buttons.Margin = Ui.Pad(0, 4, 0, 0);
            column.Controls.Add(buttons, 0, 4);
            card.Controls.Add(column);
            return card;
        }

        private Card CreateDiagnosticsCard()
        {
            var card = new Card { Dock = DockStyle.Fill, Margin = Ui.Pad(0) };
            TableLayoutPanel column = Make.Column(0, 0, 0, 100);

            _refreshButton = new ThemedButton(Strings.DiagRefresh, ButtonKind.Secondary);
            _refreshButton.Click += (s, e) => RefreshDiagnostics();
            _copyButton = new ThemedButton(Strings.DiagCopy, ButtonKind.Primary);
            _copyButton.Click += (s, e) => CopyDiagnostics();
            ThemedButton openLogs = new ThemedButton(Strings.DiagOpenLogs, ButtonKind.Secondary);
            openLogs.Click += (s, e) => OpenLogFolder();
            FlowLayoutPanel buttons = Make.Row(_copyButton, _refreshButton, openLogs);
            buttons.Margin = Ui.Pad(0, 0, 0, 12);

            _diagnosticsText = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                BorderStyle = BorderStyle.None,
                Font = ThemeFonts.Mono,
                BackColor = Palette.Background,
                ForeColor = Palette.Text,
                Margin = Ui.Pad(0)
            };
            NativeTheme.UseDarkScrollBars(_diagnosticsText);
            var textFrame = new Panel { Dock = DockStyle.Fill, Padding = Ui.Pad(10), BackColor = Palette.Background, Margin = Ui.Pad(0) };
            textFrame.Controls.Add(_diagnosticsText);

            column.Controls.Add(Make.Title(Strings.TabDiagnostics), 0, 0);
            column.Controls.Add(Make.Muted(Strings.DiagIntro), 0, 1);
            column.Controls.Add(buttons, 0, 2);
            column.Controls.Add(textFrame, 0, 3);
            card.Controls.Add(column);
            return card;
        }

        /// <summary>The Diagnostics text is collected the first time this page is shown, not at start-up.</summary>
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && !IsDisposed && !_diagnosticsLoaded)
            {
                RefreshDiagnostics();
            }
        }

        private void OnUpdatesChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) RefreshUpdateUi();
        }

        private void RefreshUpdateUi()
        {
            UpdatePhase phase = _updates.Phase;
            ReleaseInfo available = _updates.Available;
            DownloadProgress progress = _updates.Progress;

            string text;
            switch (phase)
            {
                case UpdatePhase.Checking:
                    text = Strings.UpdateStatusChecking;
                    break;
                case UpdatePhase.Downloading:
                    long total = progress != null && progress.Total > 0 ? progress.Total
                        : (available != null && available.Installer != null ? available.Installer.Size : 0);
                    text = string.Format(Strings.UpdateDownloading, available == null ? "?" : AppVersions.Format(available.Version),
                        Megabytes(progress == null ? 0 : progress.Done), Megabytes(total));
                    break;
                case UpdatePhase.Installing:
                    text = Strings.UpdateStarting;
                    break;
                default:
                    text = IdleUpdateText();
                    break;
            }
            _updateStatus.Text = text;
            _updateStatus.ForeColor = available != null && phase == UpdatePhase.Idle ? Palette.Warning : Palette.Muted;

            bool idle = phase == UpdatePhase.Idle;
            _checkNowButton.Enabled = idle;
            _updateNowButton.Visible = available != null;
            _updateNowButton.Enabled = idle;
            if (_updateAutoCheck.Checked != _updates.AutoCheck)
            {
                _updateAutoCheck.Checked = _updates.AutoCheck;
            }

            bool busy = phase == UpdatePhase.Downloading || phase == UpdatePhase.Installing;
            _updateProgress.Visible = busy;
            if (busy)
            {
                bool known = phase == UpdatePhase.Downloading && progress != null && progress.Total > 0;
                _updateProgress.Marquee = !known;
                if (known)
                {
                    _updateProgress.Value = (int)Math.Max(0, Math.Min(100, progress.Done * 100 / progress.Total));
                }
            }
            else
            {
                _updateProgress.Marquee = false;
            }
        }

        private string IdleUpdateText()
        {
            if (!string.IsNullOrEmpty(_updates.LastInstallError))
            {
                return _updates.LastInstallError;
            }
            UpdateCheckResult result = _updates.LastResult;
            if (result == null)
            {
                return _updates.AutoCheck ? Strings.UpdateStatusNeverChecked : Strings.UpdateStatusOff;
            }
            string when = FormatTime(result.CheckedAt);
            switch (result.Outcome)
            {
                case UpdateCheckOutcome.UpdateAvailable:
                    return string.Format(Strings.UpdateStatusAvailable, AppVersions.Format(result.Release.Version), AppInfo.Version);
                case UpdateCheckOutcome.Failed:
                    return string.Format(Strings.UpdateStatusFailed, result.Error, when);
                default:
                    return string.Format(Strings.UpdateStatusUpToDate, AppInfo.Version, when);
            }
        }

        private async void CheckForUpdatesNow()
        {
            try
            {
                await _updates.CheckNowAsync();
            }
            catch (Exception ex)
            {
                Log.Error("Update check: unexpected failure from the Check now button.", ex);
                Message?.Invoke(this, ex.Message);
            }
        }

        /// <summary>Update now, from this page or from the strip above the pages.</summary>
        public async void StartUpdate()
        {
            try
            {
                string problem = await _updates.InstallAsync();
                if (!string.IsNullOrEmpty(problem) && !IsDisposed)
                {
                    MessageBox.Show(this, problem, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Update: unexpected failure from the Update now button.", ex);
                Message?.Invoke(this, ex.Message);
            }
        }

        private static string Megabytes(long bytes)
        {
            return (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static string FormatTime(DateTime time)
        {
            return time.Date == DateTime.Today
                ? time.ToString("HH:mm", CultureInfo.InvariantCulture)
                : time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }

        public void RefreshDiagnostics()
        {
            if (_collecting)
            {
                return;
            }
            _collecting = true;
            _diagnosticsLoaded = true;
            SetDiagnosticsButtonsEnabled(false);
            _diagnosticsText.Text = Strings.DiagCollecting;
            Log.Info("Diagnostics: collecting.");

            AppPaths paths = _paths;
            AppConfig config = _config;
            string[] hostLines = new List<string>(_host.DescribeForDiagnostics()).ToArray();
            string[] clientLines = new List<string>(_client.DescribeForDiagnostics()).ToArray();
            string[] updateLines = new List<string>(_updates.DescribeForDiagnostics()).ToArray();
            Task.Factory.StartNew(() => CollectDiagnosticsText(paths, config, hostLines, clientLines, updateLines))
                .ContinueWith(task =>
                {
                    string text;
                    if (task.IsFaulted)
                    {
                        Exception ex = task.Exception == null ? null : task.Exception.GetBaseException();
                        Log.Error("Diagnostics: collection failed.", ex);
                        text = string.Format(Strings.DiagFailed, ex == null ? "?" : ex.Message);
                    }
                    else
                    {
                        text = task.Result;
                        Log.Info("Diagnostics: collected " + text.Length + " characters.");
                    }

                    if (IsDisposed)
                    {
                        return;
                    }
                    _diagnosticsText.Text = text;
                    _diagnosticsText.SelectionStart = 0;
                    _diagnosticsText.SelectionLength = 0;
                    _diagnosticsText.ScrollToCaret();
                    _collecting = false;
                    SetDiagnosticsButtonsEnabled(true);
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Runs on a background thread: never touches controls.</summary>
        private static string CollectDiagnosticsText(AppPaths paths, AppConfig config, string[] hostLines, string[] clientLines,
            string[] updateLines)
        {
            DiagnosticsReport report = DiagnosticsReport.Collect(paths, config, r =>
            {
                r.AddSection(Strings.DiagHostSection, hostLines);
                r.AddSection(Strings.DiagClientSection, clientLines);
                r.AddSection(Strings.DiagUpdatesSection, updateLines);
                r.AddSection(Strings.DiagPrintersSection, DescribePrinters());
            });
            return report.ToString();
        }

        private static IEnumerable<string> DescribePrinters()
        {
            var lines = new List<string>();
            try
            {
                string defaultPrinter = "";
                try
                {
                    defaultPrinter = new PrinterSettings().PrinterName ?? "";
                }
                catch (Exception ex)
                {
                    Log.Warn("Diagnostics: default printer not readable: " + ex.Message);
                }

                foreach (string name in PrinterSettings.InstalledPrinters)
                {
                    lines.Add("- " + name + (string.Equals(name, defaultPrinter, StringComparison.OrdinalIgnoreCase)
                        ? Strings.DiagDefaultPrinterSuffix : ""));
                }
                if (lines.Count == 0)
                {
                    lines.Add(Strings.DiagNoPrinters);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Diagnostics: printer list not readable: " + ex.Message);
                lines.Add(string.Format(Strings.DiagPrintersError, ex.Message));
            }
            return lines;
        }

        private void SetDiagnosticsButtonsEnabled(bool enabled)
        {
            _refreshButton.Enabled = enabled;
            _copyButton.Enabled = enabled;
        }

        private void CopyDiagnostics()
        {
            string text = _diagnosticsText.Text;
            if (string.IsNullOrEmpty(text) || _collecting)
            {
                return;
            }
            try
            {
                Clipboard.SetText(text);
                Log.Info("Diagnostics: copied to the clipboard.");
                Message?.Invoke(this, Strings.DiagCopied);
            }
            catch (Exception ex)
            {
                Log.Warn("Diagnostics: clipboard copy failed: " + ex.Message);
                Message?.Invoke(this, Strings.DiagCopyFailed);
            }
        }

        private void OpenLogFolder()
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + _paths.LogsDir + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warn("Could not open the log folder in Explorer: " + ex.Message);
                Message?.Invoke(this, string.Format(Strings.DiagOpenLogsFailed, _paths.LogsDir));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _updates.Changed -= OnUpdatesChanged;
            }
            base.Dispose(disposing);
        }
    }
}
