using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrintVect.Core;
using PrintVect.Core.Config;
using PrintVect.Core.Diagnostics;
using PrintVect.Core.Logging;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// The single window with four tabs (brief, section 7). Built in code rather than with the
    /// designer so it stays readable in a diff. The Share and Use tabs fill up in M1 and M3.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private const int MessageDisplaySeconds = 8;

        private readonly AppPaths _paths;
        private readonly ConfigStore _store;
        private readonly AppConfig _config;

        private TabControl _tabs;
        private TabPage _diagnosticsTab;
        private TextBox _settingsText;
        private TextBox _diagnosticsText;
        private Button _refreshButton;
        private Button _copyButton;
        private Button _openLogsButton;
        private ToolStripStatusLabel _sharingLabel;
        private ToolStripStatusLabel _messageLabel;
        private Timer _messageTimer;
        private bool _diagnosticsLoaded;
        private bool _collecting;

        public MainForm(AppPaths paths, ConfigStore store, AppConfig config)
        {
            _paths = paths;
            _store = store;
            _config = config;
            BuildLayout();
            FillSettings();
            UpdateSharingStatus(_config.SharingEnabled);
        }

        public void UpdateSharingStatus(bool sharingOn)
        {
            _sharingLabel.Text = sharingOn ? Strings.StatusSharingOn : Strings.StatusSharingOff;
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
            _tabs.TabPages.Add(CreatePlaceholderTab(Strings.TabShare, Strings.SharePlaceholder));
            _tabs.TabPages.Add(CreatePlaceholderTab(Strings.TabUse, Strings.UsePlaceholder));
            _tabs.TabPages.Add(CreateSettingsTab());
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
            Controls.Add(status);
            ResumeLayout(true);
        }

        private static TabPage CreatePlaceholderTab(string title, string text)
        {
            var page = new TabPage(title) { Padding = new Padding(16), UseVisualStyleBackColor = true };
            var label = new Label
            {
                Text = text,
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 64,
                TextAlign = ContentAlignment.TopLeft
            };
            page.Controls.Add(label);
            return page;
        }

        private TabPage CreateSettingsTab()
        {
            var page = new TabPage(Strings.TabSettings) { Padding = new Padding(16), UseVisualStyleBackColor = true };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

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
            page.Controls.Add(layout);
            return page;
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
            Task.Factory.StartNew(() => CollectDiagnosticsText(paths, config))
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
        private static string CollectDiagnosticsText(AppPaths paths, AppConfig config)
        {
            DiagnosticsReport report = DiagnosticsReport.Collect(paths, config,
                r => r.AddSection(Strings.DiagPrintersSection, DescribePrinters()));
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
