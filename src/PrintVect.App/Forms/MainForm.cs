using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrintVect.App.Client;
using PrintVect.App.Host;
using PrintVect.App.Update;
using PrintVect.Core;
using PrintVect.Core.Config;
using PrintVect.Core.Diagnostics;
using PrintVect.Core.Logging;
using PrintVect.Core.Update;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// The single window with four tabs (brief, section 7). Built in code rather than with the
    /// designer so it stays readable in a diff. The Share tab arrived in M1, the Use tab in M2,
    /// the Updates group of the Settings tab and the update banner above the tabs in M6a.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private const int MessageDisplaySeconds = 8;

        private readonly AppPaths _paths;
        private readonly ConfigStore _store;
        private readonly AppConfig _config;
        private readonly HostController _controller;
        private readonly ClientController _client;
        private readonly UpdateController _updates;

        private TabControl _tabs;
        private ShareTab _shareTab;
        private UseTab _useTab;
        private TabPage _settingsTab;
        private TabPage _diagnosticsTab;
        private TextBox _settingsText;
        private TextBox _diagnosticsText;
        private Button _refreshButton;
        private Button _copyButton;
        private Button _openLogsButton;
        private CheckBox _updateAutoCheck;
        private Label _updateStatus;
        private ProgressBar _updateProgress;
        private Button _checkNowButton;
        private Button _updateNowButton;
        private TableLayoutPanel _banner;
        private Label _bannerLabel;
        private Button _bannerUpdate;
        private Version _bannerDismissed;
        private ToolStripStatusLabel _sharingLabel;
        private ToolStripStatusLabel _messageLabel;
        private Timer _messageTimer;
        private bool _diagnosticsLoaded;
        private bool _collecting;

        public MainForm(AppPaths paths, ConfigStore store, AppConfig config, HostController controller, ClientController client,
            UpdateController updates)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            if (client == null) throw new ArgumentNullException(nameof(client));
            if (updates == null) throw new ArgumentNullException(nameof(updates));
            _client = client;
            _updates = updates;
            _paths = paths;
            _store = store;
            _config = config;
            _controller = controller;
            BuildLayout();
            FillSettings();
            RefreshUpdateUi();
            UpdateSharingStatus(_controller.IsSharing);
            _controller.SharingChanged += (s, e) =>
            {
                UpdateSharingStatus(_controller.IsSharing);
                FillSettings();
            };
            _updates.Changed += (s, e) => RefreshUpdateUi();
        }

        public void UpdateSharingStatus(bool sharingOn)
        {
            _sharingLabel.Text = sharingOn ? Strings.StatusSharingOn : Strings.StatusSharingOff;
        }

        /// <summary>Opens the Settings tab, where the Updates group lives (the update balloon leads here).</summary>
        public void ShowSettings()
        {
            if (!IsDisposed && _settingsTab != null)
            {
                _tabs.SelectedTab = _settingsTab;
            }
        }

        private void BuildLayout()
        {
            SuspendLayout();
            Text = Strings.AppName;
            Icon = AppIcon.Window;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(780, 540);
            MinimumSize = new Size(640, 440);
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;

            var status = new StatusStrip { SizingGrip = true };
            _sharingLabel = new ToolStripStatusLabel(Strings.StatusSharingOff)
            {
                BorderSides = ToolStripStatusLabelBorderSides.Right
            };
            _messageLabel = new ToolStripStatusLabel("") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            var versionLabel = new ToolStripStatusLabel(string.Format(Strings.StatusVersion, AppInfo.Version));
            status.Items.AddRange(new ToolStripItem[] { _sharingLabel, _messageLabel, versionLabel });

            _tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 6) };
            _tabs.TabPages.Add(CreateShareTab());
            _tabs.TabPages.Add(CreateUseTab());
            _settingsTab = CreateSettingsTab();
            _tabs.TabPages.Add(_settingsTab);
            _diagnosticsTab = CreateDiagnosticsTab();
            _tabs.TabPages.Add(_diagnosticsTab);
            _tabs.SelectedIndexChanged += (s, e) =>
            {
                if (_tabs.SelectedTab == _diagnosticsTab && !_diagnosticsLoaded)
                {
                    RefreshDiagnostics();
                }
            };

            _messageTimer = new Timer { Interval = MessageDisplaySeconds * 1000 };
            _messageTimer.Tick += (s, e) =>
            {
                _messageTimer.Stop();
                _messageLabel.Text = "";
            };

            Controls.Add(_tabs);
            Controls.Add(CreateBanner());
            Controls.Add(status);
            ResumeLayout(true);
        }

        /// <summary>A strip above the tabs that appears when a newer PrintVect is ready (M6a).</summary>
        private TableLayoutPanel CreateBanner()
        {
            _banner = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = SystemColors.Info,
                Padding = new Padding(12, 6, 12, 6),
                Visible = false
            };
            _banner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _banner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _banner.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _bannerLabel = new Label
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = SystemColors.InfoText,
                Margin = new Padding(0, 0, 8, 0)
            };
            _bannerUpdate = CreateButton(Strings.SettingsUpdateNow, (s, e) => StartUpdate());
            _bannerUpdate.Anchor = AnchorStyles.None;
            Button later = CreateButton(Strings.UpdateBannerLater, (s, e) => DismissBanner());
            later.Anchor = AnchorStyles.None;
            later.Margin = new Padding(0);

            _banner.Controls.Add(_bannerLabel, 0, 0);
            _banner.Controls.Add(_bannerUpdate, 1, 0);
            _banner.Controls.Add(later, 2, 0);
            return _banner;
        }

        private TabPage CreateShareTab()
        {
            var page = new TabPage(Strings.TabShare) { Padding = new Padding(16), UseVisualStyleBackColor = true };
            _shareTab = new ShareTab(_controller) { Dock = DockStyle.Fill };
            page.Controls.Add(_shareTab);
            return page;
        }

        private TabPage CreateUseTab()
        {
            var page = new TabPage(Strings.TabUse) { Padding = new Padding(16), UseVisualStyleBackColor = true };
            _useTab = new UseTab(_client) { Dock = DockStyle.Fill };
            page.Controls.Add(_useTab);
            return page;
        }

        private TabPage CreateSettingsTab()
        {
            var page = new TabPage(Strings.TabSettings) { Padding = new Padding(16), UseVisualStyleBackColor = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var intro = new Label
            {
                Text = Strings.SettingsIntro,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 12)
            };
            _settingsText = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window,
                Margin = new Padding(0)
            };

            layout.Controls.Add(intro, 0, 0);
            layout.Controls.Add(_settingsText, 0, 1);
            layout.Controls.Add(CreateUpdatesGroup(), 0, 2);
            page.Controls.Add(layout);
            return page;
        }

        /// <summary>The Updates group of the Settings tab (M6a): daily check switch, status, Check now, Update now.</summary>
        private GroupBox CreateUpdatesGroup()
        {
            var group = new GroupBox
            {
                Text = Strings.SettingsUpdatesGroup,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 12, 0, 0),
                Padding = new Padding(10, 4, 10, 10)
            };
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                RowCount = 4
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            }

            _updateAutoCheck = new CheckBox
            {
                Text = Strings.SettingsCheckForUpdates,
                AutoSize = true,
                Checked = _updates.AutoCheck,
                Margin = new Padding(0, 4, 0, 6)
            };
            _updateAutoCheck.CheckedChanged += (s, e) => _updates.SetAutoCheck(_updateAutoCheck.Checked);
            _updateStatus = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
            _updateProgress = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Height = 14,
                Minimum = 0,
                Maximum = 100,
                Visible = false,
                Margin = new Padding(0, 0, 0, 8)
            };
            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0)
            };
            _updateNowButton = CreateButton(Strings.SettingsUpdateNow, (s, e) => StartUpdate());
            _updateNowButton.Visible = false;
            _checkNowButton = CreateButton(Strings.SettingsCheckNow, (s, e) => CheckForUpdatesNow());
            buttons.Controls.AddRange(new Control[] { _updateNowButton, _checkNowButton });

            table.Controls.Add(_updateAutoCheck, 0, 0);
            table.Controls.Add(_updateStatus, 0, 1);
            table.Controls.Add(_updateProgress, 0, 2);
            table.Controls.Add(buttons, 0, 3);
            group.Controls.Add(table);
            return group;
        }

        private void RefreshUpdateUi()
        {
            if (IsDisposed || _updateStatus == null)
            {
                return;
            }
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
                _updateProgress.Style = known ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                if (known)
                {
                    _updateProgress.Value = (int)Math.Max(0, Math.Min(100, progress.Done * 100 / progress.Total));
                }
            }

            bool showBanner = available != null && (_bannerDismissed == null || _bannerDismissed != available.Version);
            if (showBanner)
            {
                _bannerLabel.Text = string.Format(Strings.UpdateBannerText, AppVersions.Format(available.Version));
                _bannerUpdate.Enabled = idle;
            }
            _banner.Visible = showBanner;
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
                ShowMessage(ex.Message);
            }
        }

        private async void StartUpdate()
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
                ShowMessage(ex.Message);
            }
        }

        private void DismissBanner()
        {
            ReleaseInfo available = _updates.Available;
            _bannerDismissed = available == null ? null : available.Version;
            RefreshUpdateUi();
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

        private TabPage CreateDiagnosticsTab()
        {
            var page = new TabPage(Strings.TabDiagnostics) { Padding = new Padding(16), UseVisualStyleBackColor = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var intro = new Label
            {
                Text = Strings.DiagIntro,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 10)
            };

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Margin = new Padding(0, 0, 0, 8)
            };
            _refreshButton = CreateButton(Strings.DiagRefresh, (s, e) => RefreshDiagnostics());
            _copyButton = CreateButton(Strings.DiagCopy, (s, e) => CopyDiagnostics());
            _openLogsButton = CreateButton(Strings.DiagOpenLogs, (s, e) => OpenLogFolder());
            buttons.Controls.AddRange(new Control[] { _refreshButton, _copyButton, _openLogsButton });

            _diagnosticsText = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Both,
                WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 9f),
                BackColor = SystemColors.Window,
                Margin = new Padding(0)
            };

            layout.Controls.Add(intro, 0, 0);
            layout.Controls.Add(buttons, 0, 1);
            layout.Controls.Add(_diagnosticsText, 0, 2);
            page.Controls.Add(layout);
            return page;
        }

        private static Button CreateButton(string text, EventHandler onClick)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(8, 3, 8, 3),
                Margin = new Padding(0, 0, 8, 0),
                UseVisualStyleBackColor = true
            };
            button.Click += onClick;
            return button;
        }

        private void FillSettings()
        {
            var lines = new List<string>
            {
                string.Format(Strings.SettingsConfigFile, _store.Path),
                "",
                string.Format(Strings.SettingsSharing, _config.SharingEnabled ? Strings.On : Strings.Off),
                string.Format(Strings.SettingsPorts, _config.DiscoveryPort, _config.JobPort),
                string.Format(Strings.SettingsPin, string.IsNullOrEmpty(_config.Pin) ? Strings.PinNotSet : Strings.PinSet),
                string.Format(Strings.SettingsStartWithWindows, _config.StartWithWindows ? Strings.Yes : Strings.No),
                string.Format(Strings.SettingsKeepFiles, _config.KeepSentFilesHours)
            };
            _settingsText.Lines = lines.ToArray();
        }

        private void RefreshDiagnostics()
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
            string[] hostLines = _controller.DescribeForDiagnostics().ToArray();
            string[] clientLines = _client.DescribeForDiagnostics().ToArray();
            string[] updateLines = _updates.DescribeForDiagnostics().ToArray();
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
                ShowMessage(Strings.DiagCopied);
            }
            catch (Exception ex)
            {
                Log.Warn("Diagnostics: clipboard copy failed: " + ex.Message);
                ShowMessage(Strings.DiagCopyFailed);
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
                ShowMessage(string.Format(Strings.DiagOpenLogsFailed, _paths.LogsDir));
            }
        }

        private void ShowMessage(string text)
        {
            _messageLabel.Text = text;
            _messageTimer.Stop();
            _messageTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _messageTimer != null)
            {
                _messageTimer.Dispose();
                _messageTimer = null;
            }
            base.Dispose(disposing);
        }
    }
}
