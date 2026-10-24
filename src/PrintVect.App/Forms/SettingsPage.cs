using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrintVect.App.Client;
using PrintVect.App.Host;
using PrintVect.App.Theme;
using PrintVect.App.Update;
using PrintVect.Core;
using PrintVect.Core.Config;
using PrintVect.Core.Diagnostics;
using PrintVect.Core.Elevation;
using PrintVect.Core.Logging;
using PrintVect.Core.Update;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// "Settings" in the redesign of 0.5.0. Left: Security (the office PIN), General (Colours: Light, Dark or System
    /// default; Start with Windows; the copies question; how long sent documents are kept), Updates, and the network
    /// facts for whoever supports PrintVect. Right: Diagnostics, a checklist of what PrintVect checked on this PC with
    /// the full report to copy underneath. Every change is saved at once and applies to this PC only.
    /// </summary>
    internal sealed class SettingsPage : PageScroller, IPage
    {
        private readonly AppPaths _paths;
        private readonly ConfigStore _store;
        private readonly AppConfig _config;
        private readonly HostController _host;
        private readonly ClientController _client;
        private readonly UpdateController _updates;
        private readonly IShell _shell;

        private readonly StatusPill _pinPill;
        private readonly Label _pinText;
        private readonly ThemedButton _pinSet;
        private readonly ThemedButton _pinRemove;
        private readonly Segmented _colours;
        private readonly ToggleSwitch _autostart;
        private readonly ToggleSwitch _askCopies;
        private readonly Stepper _keep;
        private readonly ToggleSwitch _autoCheck;
        private readonly GlyphBox _updateIcon;
        private readonly Label _updateStatus;
        private readonly ThinProgress _updateProgress;
        private readonly ThemedButton _updateNow;
        private readonly ThemedButton _checkNow;
        private readonly TableLayoutPanel _checks;
        private readonly ThemedButton _copy;
        private readonly ThemedButton _refresh;
        private readonly ThemedButton _showReport;
        private readonly TextBox _report;
        private readonly Panel _reportFrame;
        private bool _syncing;
        private bool _collecting;
        private bool _loaded;

        public SettingsPage(AppPaths paths, ConfigStore store, AppConfig config, HostController host, ClientController client,
            UpdateController updates, IShell shell)
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
            _shell = shell;

            Add(Make.Stack(6, Make.PageTitle(Strings.TabSettings), Make.Intro(Strings.SettingsIntro)), 0);

            // Security
            _pinPill = new StatusPill { Anchor = AnchorStyles.Left, Margin = Ui.Pad(10, 0, 0, 0) };
            _pinText = Make.Muted("");
            _pinSet = new ThemedButton("", ButtonKind.Primary, Glyph.Lock) { Margin = Ui.Pad(0, 0, 8, 0) };
            _pinSet.Click += (s, e) => ChangePin();
            _pinRemove = new ThemedButton(Strings.SettingsPinRemove, ButtonKind.Danger) { Margin = Ui.Pad(0) };
            _pinRemove.Click += (s, e) => RemovePin();
            FlowLayoutPanel pinButtons = Make.Row(_pinSet, _pinRemove);
            pinButtons.WrapContents = false;
            Label pinTitle = Make.Label(Strings.SettingsPinCaption, ThemeFonts.TextSemi, Palette.Text, Ui.Pad(0));
            pinTitle.Anchor = AnchorStyles.Left;
            var pinRow = new SplitRow(null, Make.Stack(4, Make.Row(pinTitle, _pinPill), _pinText), pinButtons, 200, 16);
            Card security = Make.Card(Make.Stack(12, Make.CardTitle(Strings.SettingsSecurity), pinRow), 20, 18);

            // General
            _colours = new Segmented(Strings.SettingsColoursLight, Strings.SettingsColoursDark, Strings.SettingsColoursSystem);
            _colours.SelectedIndex = ColourIndex(ThemeManager.Mode);
            _colours.SelectedIndexChanged += (s, e) => ChangeColours();
            _autostart = new ToggleSwitch { AccessibleName = Strings.SettingsAutostartTitle };
            _autostart.CheckedChanged += (s, e) => ChangeAutostart();
            _askCopies = new ToggleSwitch { AccessibleName = Strings.SettingsAskCopiesTitle };
            _askCopies.CheckedChanged += (s, e) =>
            {
                if (!_syncing && !_client.SetAskCopies(_askCopies.Checked)) SaveFailed();
            };
            _keep = new Stepper(Strings.SettingsFewerHours, Strings.SettingsMoreHours);
            _keep.Less += (s, e) => ChangeKeepHours(-1);
            _keep.More += (s, e) => ChangeKeepHours(+1);
            TableLayoutPanel general = Make.Stack(0, Make.CardTitle(Strings.SettingsGeneral),
                SettingRow(Strings.SettingsColours, Strings.SettingsColoursNote, _colours, false),
                Make.Divider(0, 0, true),
                SettingRow(Strings.SettingsAutostartTitle, Strings.SettingsAutostartNote, _autostart, false),
                Make.Divider(0, 0, true),
                SettingRow(Strings.SettingsAskCopiesTitle, Strings.SettingsAskCopiesNote, _askCopies, false),
                Make.Divider(0, 0, true),
                SettingRow(Strings.SettingsKeepTitle, Strings.SettingsKeepNote, _keep, true));
            Card generalCard = Make.Card(general, 20, 18);

            // Updates
            _autoCheck = new ToggleSwitch { Checked = _updates.AutoCheck, AccessibleName = Strings.SettingsCheckDaily };
            _autoCheck.CheckedChanged += (s, e) => _updates.SetAutoCheck(_autoCheck.Checked);
            _updateIcon = new GlyphBox(Glyph.Check, 16, Palette.AccentText, 2.2f) { Margin = Ui.Pad(0, 0, 8, 0) };
            _updateStatus = Make.Wrap("", ThemeFonts.SecondarySemi, Palette.AccentText, Ui.Pad(0));
            _updateProgress = new ThinProgress { Visible = false, Margin = Ui.Pad(0) };
            _updateNow = new ThemedButton(Strings.SettingsUpdateNow, ButtonKind.Primary) { Visible = false, Margin = Ui.Pad(0, 0, 8, 0) };
            _updateNow.Click += (s, e) => StartUpdate();
            _checkNow = new ThemedButton(Strings.SettingsCheckNow, ButtonKind.Secondary) { Margin = Ui.Pad(0) };
            _checkNow.Click += (s, e) => CheckForUpdatesNow();
            FlowLayoutPanel updateButtons = Make.Row(_updateNow, _checkNow);
            updateButtons.WrapContents = false;
            var status = new SplitRow(Make.Row(_updateIcon), _updateStatus, updateButtons, 180, 0);   // the tick hides inside its row
            Card updatesCard = Make.Card(Make.Stack(10, Make.CardTitle(Strings.SettingsUpdatesGroup),
                SettingRow(Strings.SettingsCheckDaily, Strings.SettingsCheckDailyNote, _autoCheck, false), status, _updateProgress), 20, 18);

            // Network facts
            var openFolder = new ThemedButton(Strings.SettingsOpenFolder, ButtonKind.Secondary, Glyph.Folder) { Margin = Ui.Pad(0) };
            openFolder.Click += (s, e) => OpenFolder(Path.GetDirectoryName(_store.Path));
            FlowLayoutPanel chips = Make.Row(Chip(Strings.SettingsFindsPrinters, "UDP " + _config.DiscoveryPort),
                Chip(Strings.SettingsCarriesJobs, "TCP " + _config.JobPort));
            // The button moves under the path when the path does not fit on one line beside it: it broke inside a word
            // ("confi" / "g.json", owner's photo, 0.5.2).
            int pathWidth = (int)Math.Ceiling(TextRenderer.MeasureText(_store.Path, ThemeFonts.Small).Width / Ui.Scale) + 4;
            var configRow = new SplitRow(null, Make.Wrap(_store.Path, ThemeFonts.Small, Palette.TextSecondary, Ui.Pad(0)), openFolder,
                Math.Max(160, pathWidth), 12);
            Card networkCard = Make.Card(Make.Stack(10, Make.CardTitle(Strings.SettingsNetworkTitle), chips,
                Make.Muted(Strings.SettingsPortsNote), configRow), 20, 18);

            // Diagnostics
            _copy = new ThemedButton(Strings.DiagCopyReport, ButtonKind.Primary, Glyph.Copy);
            _copy.Click += (s, e) => CopyReport();
            var openLogs = new ThemedButton(Strings.DiagOpenLogs, ButtonKind.Secondary, Glyph.Folder);
            openLogs.Click += (s, e) => OpenFolder(_paths.LogsDir);
            _refresh = new ThemedButton(Strings.DiagRefresh, ButtonKind.Ghost, Glyph.Retry) { Margin = Ui.Pad(0) };
            _refresh.Click += (s, e) => RefreshDiagnostics();
            _checks = Make.Stack(0);
            _showReport = new ThemedButton(Strings.DiagShowReport, ButtonKind.Ghost, Glyph.Chevron) { Margin = Ui.Pad(0) };
            _showReport.Click += (s, e) => ToggleReport();
            _report = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                BorderStyle = BorderStyle.None,
                Font = ThemeFonts.Report,
                BackColor = Palette.Report,
                ForeColor = Palette.TextSecondary,
                Margin = Ui.Pad(0)
            };
            NativeTheme.UseThemedScrollBars(_report);
            _reportFrame = new Card { Radius = 8, Fill = Palette.Report, Edge = Palette.NestedBorder, BackColor = Palette.Report, Padding = Ui.Pad(14, 12, 6, 12), Height = Ui.S(240), Visible = false };
            _reportFrame.Controls.Add(_report);
            Card diagnostics = Make.Card(Make.Stack(14,
                Make.Stack(4, Make.CardTitle(Strings.TabDiagnostics), Make.Muted(Strings.DiagIntro)),
                Make.Row(_copy, openLogs, _refresh), _checks, _showReport, _reportFrame), 20, 18);

            TableLayoutPanel leftColumn = Make.Stack(16, security, generalCard, updatesCard, networkCard);
            Add(new TwoColumns(leftColumn, diagnostics, 380, 1, 420, 1.15f, 22), 22);

            _client.SettingsChanged += OnClientSettingsChanged;
            _updates.Changed += OnUpdatesChanged;
            _host.SharingChanged += OnSharingChanged;
            ShowValues();
            RefreshUpdateUi();
        }

        private static int ColourIndex(ThemeMode mode)
        {
            return mode == ThemeMode.Light ? 0 : mode == ThemeMode.Dark ? 1 : 2;
        }

        /// <summary>A setting: its name and a grey line under it on the left, the switch or box on the right.</summary>
        private static Control SettingRow(string title, string note, Control control, bool last)
        {
            TableLayoutPanel text = Make.Stack(2, Make.Label(title, ThemeFonts.TextSemi, Palette.Text, Ui.Pad(0)), Make.Muted(note));
            var row = new SplitRow(null, text, control, 200, 16);
            row.Margin = Ui.Pad(0, 12, 0, last ? 4 : 12);
            return row;
        }

        /// <summary>"Finds printers UDP 9150" in a small grey box.</summary>
        private static Control Chip(string label, string value)
        {
            Label a = Make.Label(label + " ", ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0));
            Label b = Make.Label(value, ThemeFonts.MonoSemi, Palette.Text, Ui.Pad(0, 1, 0, 0));
            var chip = new Card { Radius = 6, Fill = Palette.DisabledBg, Edge = Color.Transparent, BackColor = Palette.DisabledBg, Padding = Ui.Pad(10, 5, 10, 5), AutoSize = true, Margin = Ui.Pad(0, 0, 8, 0) };
            FlowLayoutPanel content = Make.Row(a, b);
            content.WrapContents = false;
            content.Dock = DockStyle.Fill;
            chip.Controls.Add(content);
            return chip;
        }

        public void SetOpen(bool open)
        {
            if (open) LoadDiagnostics();
        }

        /// <summary>
        /// Collects the Diagnostics checklist once, not at start-up: when the window opens (so it is ready, and its card
        /// does not grow under the eyes, when Settings is opened) or when this page is.
        /// </summary>
        public void LoadDiagnostics()
        {
            if (!IsDisposed && !_loaded) RefreshDiagnostics();
        }

        // ---- Security and General ----

        private void OnClientSettingsChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) ShowValues();
        }

        private void ShowValues()
        {
            _syncing = true;
            try
            {
                bool hasPin = !string.IsNullOrEmpty(_config.Pin);
                _pinPill.Set(hasPin ? Strings.SettingsPinSetPill : Strings.SettingsPinNotSetPill, hasPin ? PillKind.Success : PillKind.Warning);
                _pinText.Text = hasPin ? Strings.SettingsPinOn : Strings.SettingsPinOff;
                _pinSet.Text = hasPin ? Strings.SettingsPinChange : Strings.SettingsPinSet;
                _pinSet.Kind = hasPin ? ButtonKind.Secondary : ButtonKind.Primary;
                _pinSet.Font = hasPin ? ThemeFonts.TextMedium : ThemeFonts.TextSemi;
                _pinRemove.Visible = hasPin;

                bool? autostart = Autostart.IsOn();
                _autostart.Checked = autostart ?? _config.StartWithWindows;
                _autostart.Enabled = autostart.HasValue;
                _askCopies.Checked = _client.AskCopies;
                _keep.Value = string.Format(CultureInfo.CurrentCulture, Strings.SettingsKeepHours, _config.KeepSentFilesHours);
            }
            finally
            {
                _syncing = false;
            }
        }

        private void ChangeColours()
        {
            ThemeMode mode = _colours.SelectedIndex == 0 ? ThemeMode.Light : _colours.SelectedIndex == 1 ? ThemeMode.Dark : ThemeMode.System;
            _config.Theme = ThemeManager.ToConfig(mode);
            Save();
            ThemeManager.SetMode(mode);   // the window builds itself again in the new colours
        }

        private void ChangePin()
        {
            using (var dialog = new PinDialog(!string.IsNullOrEmpty(_config.Pin)))
            {
                if (dialog.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    if (PinSetting.Save(_store, _config, _host, _client, dialog.Pin)) _shell.Say(Strings.SettingsPinSaved);
                    else SaveFailed();
                    ShowValues();
                }
            }
        }

        private void RemovePin()
        {
            if (MessageBox.Show(FindForm(), Strings.SettingsPinRemoveConfirm, Strings.AppName, MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.Yes)
            {
                if (PinSetting.Save(_store, _config, _host, _client, "")) _shell.Say(Strings.SettingsPinRemoved);
                else SaveFailed();
                ShowValues();
            }
        }

        private void ChangeKeepHours(int delta)
        {
            int hours = Math.Max(1, Math.Min(AppConfig.MaxKeepSentFilesHours, _config.KeepSentFilesHours + delta));
            if (hours == _config.KeepSentFilesHours)
            {
                return;
            }
            _config.KeepSentFilesHours = hours;
            Save();
            _client.ApplyKeepSentFilesHours(hours);
            ShowValues();
        }

        /// <summary>The Run entry is per machine, so PrintVect.Elevate changes it; Windows asks for permission once.</summary>
        private async void ChangeAutostart()
        {
            if (_syncing)
            {
                return;
            }
            bool on = _autostart.Checked;
            _autostart.Enabled = false;
            try
            {
                var arguments = new ElevateArguments { Command = ElevateCommands.Autostart };
                arguments.Options[ElevateCommands.OptionOn] = on ? "yes" : "no";
                ElevateResult result = await ElevateLauncher.RunAsync(_paths, arguments);
                if (IsDisposed) return;
                if (result.Ok)
                {
                    _config.StartWithWindows = on;
                    Save();
                }
                _shell.Say(result.Message);
            }
            catch (Exception ex)
            {
                Log.Error("Changing Start with Windows failed.", ex);
                _shell.Say(ex.Message);
            }
            finally
            {
                if (!IsDisposed) ShowValues();   // shows what Windows really has now
            }
        }

        private bool Save()
        {
            try
            {
                _store.Save(_config);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("Could not save " + _store.Path, ex);
                SaveFailed();
                return false;
            }
        }

        private void SaveFailed()
        {
            MessageBox.Show(FindForm(), string.Format(Strings.SharingSaveError, _store.Path), Strings.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ---- Updates ----

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
            bool good = false, warn = false;
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
                    text = IdleUpdateText(out good, out warn);
                    break;
            }
            _updateStatus.Text = text;
            _updateStatus.ForeColor = good ? Palette.AccentText : warn ? Palette.WarningText : Palette.Muted;
            _updateStatus.Font = good ? ThemeFonts.SecondarySemi : ThemeFonts.Secondary;
            _updateIcon.Visible = good;

            bool idle = phase == UpdatePhase.Idle;
            _checkNow.Enabled = idle;
            _updateNow.Visible = available != null;
            _updateNow.Enabled = idle;
            if (_autoCheck.Checked != _updates.AutoCheck)
            {
                _autoCheck.Checked = _updates.AutoCheck;
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

        private string IdleUpdateText(out bool good, out bool warn)
        {
            good = false;
            warn = false;
            if (!string.IsNullOrEmpty(_updates.LastInstallError))
            {
                warn = true;
                return _updates.LastInstallError;
            }
            UpdateCheckResult result = _updates.LastResult;
            if (result == null)
            {
                return _updates.AutoCheck ? Strings.UpdateStatusNeverChecked : Strings.UpdateStatusOff;
            }
            string when = Texts.When(result.CheckedAt);
            switch (result.Outcome)
            {
                case UpdateCheckOutcome.UpdateAvailable:
                    warn = true;
                    return string.Format(Strings.UpdateStatusAvailable, AppVersions.Format(result.Release.Version), AppInfo.Version);
                case UpdateCheckOutcome.Failed:
                    warn = true;
                    return string.Format(Strings.UpdateStatusFailed, result.Error, when);
                default:
                    good = true;
                    return string.Format(Strings.UpdateStatusLatest, AppInfo.Version, when);
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
                _shell.Say(ex.Message);
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
                    MessageBox.Show(FindForm(), problem, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Update: unexpected failure from the Update now button.", ex);
                _shell.Say(ex.Message);
            }
        }

        private static string Megabytes(long bytes)
        {
            return (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture);
        }

        // ---- Diagnostics ----

        private void OnSharingChanged(object sender, EventArgs e)
        {
            if (!IsDisposed && _loaded) RefreshDiagnostics();
        }

        private void ToggleReport()
        {
            bool show = !_reportFrame.Visible;
            _reportFrame.Visible = show;
            _showReport.Text = show ? Strings.DiagHideReport : Strings.DiagShowReport;
            _showReport.Glyph = show ? Glyph.ChevronDown : Glyph.Chevron;
        }

        /// <summary>What the checklist shows, collected on a background thread with the report.</summary>
        private sealed class Snapshot
        {
            public string Report;
            public FirewallRuleState Tcp, Udp;
            public string FirewallDetail;
            public IList<string> Folders;
            public IList<NetworkConnection> Networks;
            public string Windows, DotNet;
            public DateTime Started;
        }

        public void RefreshDiagnostics()
        {
            if (_collecting)
            {
                return;
            }
            _collecting = true;
            _loaded = true;
            _copy.Enabled = false;
            _refresh.Enabled = false;
            _report.Text = Strings.DiagCollecting;
            Log.Info("Diagnostics: collecting.");

            AppPaths paths = _paths;
            AppConfig config = _config;
            string[] hostLines = _host.DescribeForDiagnostics().ToArray();
            string[] clientLines = _client.DescribeForDiagnostics().ToArray();
            string[] updateLines = _updates.DescribeForDiagnostics().ToArray();
            Task.Run(() => Collect(paths, config, hostLines, clientLines, updateLines)).ContinueWith(task =>
            {
                if (IsDisposed)
                {
                    return;
                }
                _collecting = false;
                _copy.Enabled = true;
                _refresh.Enabled = true;
                if (task.IsFaulted)
                {
                    Exception ex = task.Exception == null ? null : task.Exception.GetBaseException();
                    Log.Error("Diagnostics: collection failed.", ex);
                    _report.Text = string.Format(Strings.DiagFailed, ex == null ? "?" : ex.Message);
                    return;
                }
                Snapshot snapshot = task.Result;
                Log.Info("Diagnostics: collected " + snapshot.Report.Length + " characters.");
                _report.Text = snapshot.Report;
                _report.SelectionStart = 0;
                _report.SelectionLength = 0;
                ShowChecks(snapshot);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Runs on a background thread: never touches controls.</summary>
        private static Snapshot Collect(AppPaths paths, AppConfig config, string[] hostLines, string[] clientLines, string[] updateLines)
        {
            var snapshot = new Snapshot();
            snapshot.Report = DiagnosticsReport.Collect(paths, config, r =>
            {
                r.AddSection(Strings.DiagHostSection, hostLines);
                r.AddSection(Strings.DiagClientSection, clientLines);
                r.AddSection(Strings.DiagUpdatesSection, updateLines);
                r.AddSection(Strings.DiagPrintersSection, DescribePrinters());
            }).ToString();
            string tcpDetail, udpDetail;
            snapshot.Tcp = FirewallRules.Query(FirewallRules.JobsRuleName, out tcpDetail);
            snapshot.Udp = FirewallRules.Query(FirewallRules.DiscoveryRuleName, out udpDetail);
            snapshot.FirewallDetail = snapshot.Tcp == FirewallRuleState.Unknown ? tcpDetail : udpDetail;
            snapshot.Folders = DiagnosticsReport.UnwritableFolders(paths);
            snapshot.Networks = NetworkProfiles.Connected();
            snapshot.Windows = WindowsInfo.Edition();
            snapshot.DotNet = NetFrameworkInfo.Describe();
            snapshot.Started = Process.GetCurrentProcess().StartTime;
            return snapshot;
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
                    lines.Add("- " + name + (string.Equals(name, defaultPrinter, StringComparison.OrdinalIgnoreCase) ? Strings.DiagDefaultPrinterSuffix : ""));
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

        private enum CheckLevel
        {
            Ok,
            Info,
            Warning,
            Neutral
        }

        /// <summary>The checklist: one line per thing PrintVect checked, a green tick, a blue i, a yellow ! or a grey dash.</summary>
        private void ShowChecks(Snapshot s)
        {
            var items = new List<Tuple<CheckLevel, string, string>>();
            items.Add(Tuple.Create(CheckLevel.Ok, AppInfo.ProductName + " " + AppInfo.Version,
                string.Format(Strings.CheckProgram, Environment.Is64BitProcess ? "64-bit" : "32-bit", Texts.When(s.Started))));

            int build = s.Windows.IndexOf(" (build", StringComparison.OrdinalIgnoreCase);
            string edition = build > 0 ? s.Windows.Substring(0, build) : s.Windows;
            string buildText = build > 0 ? s.Windows.Substring(build + 2).TrimEnd(')') : "";
            items.Add(Tuple.Create(WindowsInfo.IsWindows ? CheckLevel.Ok : CheckLevel.Neutral, edition,
                (buildText.Length > 0 ? char.ToUpperInvariant(buildText[0]) + buildText.Substring(1) + " · " : "") + ".NET Framework " + s.DotNet));

            if (s.Tcp == FirewallRuleState.Present && s.Udp == FirewallRuleState.Present)
                items.Add(Tuple.Create(CheckLevel.Ok, Strings.CheckFirewall, string.Format(Strings.CheckFirewallOk, _config.JobPort, _config.DiscoveryPort)));
            else if (s.Tcp == FirewallRuleState.Missing || s.Udp == FirewallRuleState.Missing)
                items.Add(Tuple.Create(CheckLevel.Warning, Strings.CheckFirewallMissing,
                    string.Format(Strings.CheckFirewallMissingText, s.Tcp == FirewallRuleState.Missing ? "TCP " + _config.JobPort : "UDP " + _config.DiscoveryPort)));
            else
                items.Add(Tuple.Create(CheckLevel.Neutral, Strings.CheckFirewallUnknown, s.FirewallDetail ?? ""));

            items.Add(s.Folders.Count == 0
                ? Tuple.Create(CheckLevel.Ok, Strings.CheckFolders, Strings.CheckFoldersOk)
                : Tuple.Create(CheckLevel.Warning, Strings.CheckFoldersProblem, s.Folders[0]));

            bool sharing = _host.IsSharing;
            List<NetworkConnection> publicOnes = NetworkProfiles.PublicNetworks(s.Networks).ToList();
            string cards = string.Join(" " + Strings.WordAnd + " ", s.Networks.Select(n => n.Adapter + " " + string.Join(", ", n.Addresses)));
            if (s.Networks.Count == 0 && WindowsInfo.IsWindows)
                items.Add(Tuple.Create(CheckLevel.Warning, Strings.CheckNoNetwork, Strings.CheckNoNetworkText));
            foreach (NetworkConnection pub in publicOnes)
                items.Add(Tuple.Create(sharing ? CheckLevel.Warning : CheckLevel.Info, string.Format(Strings.CheckPublic, pub.Network),
                    sharing ? Strings.CheckPublicSharing : Strings.CheckPublicNotSharing));
            if (s.Networks.Count > 1)
                items.Add(Tuple.Create(CheckLevel.Info, string.Format(Strings.CheckNetworks, s.Networks.Count), string.Format(Strings.CheckNetworksText, cards)));
            else if (s.Networks.Count == 1)
                items.Add(Tuple.Create(CheckLevel.Ok, Strings.CheckNetwork, cards + " · " + CategoryText(s.Networks[0].Category)));

            items.Add(string.IsNullOrEmpty(_config.Pin)
                ? Tuple.Create(CheckLevel.Warning, Strings.CheckPinOff, sharing ? Strings.CheckPinOffSharing : Strings.CheckPinOffText)
                : Tuple.Create(CheckLevel.Ok, Strings.CheckPinOn, Strings.CheckPinOnText));

            int remote = _client.Printers.Count;
            items.Add(sharing
                ? Tuple.Create(CheckLevel.Ok, Strings.CheckSharingOn, string.Format(Strings.CheckSharingOnText, _config.SharedPrinters.Count, remote))
                : Tuple.Create(CheckLevel.Neutral, Strings.CheckSharingOff, string.Format(Strings.CheckSharingOffText, remote)));

            _checks.SuspendLayout();
            foreach (Control old in _checks.Controls.Cast<Control>().ToList()) old.Dispose();
            _checks.Controls.Clear();
            _checks.RowStyles.Clear();
            _checks.RowCount = 0;
            foreach (Tuple<CheckLevel, string, string> item in items)
            {
                if (_checks.RowCount > 0) AddCheckRow(Make.Divider(0, 0, true));
                AddCheckRow(CheckRow(item.Item1, item.Item2, item.Item3));
            }
            Ui.Solidify(_checks);
            _checks.ResumeLayout(true);
        }

        private void AddCheckRow(Control control)
        {
            control.Dock = DockStyle.Fill;
            if (!(control.GetType() == typeof(Panel))) control.Margin = Ui.Pad(0, 11, 0, 11);
            _checks.RowCount++;
            _checks.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _checks.Controls.Add(control, 0, _checks.RowCount - 1);
        }

        private static Control CheckRow(CheckLevel level, string title, string detail)
        {
            Glyph glyph;
            Color fill, ink;
            switch (level)
            {
                case CheckLevel.Ok: glyph = Glyph.Check; fill = Palette.SuccessBg; ink = Palette.Accent; break;
                case CheckLevel.Info: glyph = Glyph.InfoMark; fill = Palette.InfoBg; ink = Palette.InfoIcon; break;
                case CheckLevel.Warning: glyph = Glyph.Warning; fill = Palette.WarningBg; ink = Palette.WarningText; break;
                default: glyph = Glyph.Dash; fill = Palette.NeutralBg; ink = Palette.NeutralText; break;
            }
            var tile = new IconTile(glyph, 26, 14, fill, ink, true, 13, 2.6f);
            TableLayoutPanel text = Make.Stack(1, Make.Label(title, ThemeFonts.TextSemi, Palette.Text, Ui.Pad(0)), Make.Muted(detail));
            return new SplitRow(tile, text, null, 120, 12) { AlignTop = true };
        }

        private static string CategoryText(NetworkCategory category)
        {
            switch (category)
            {
                case NetworkCategory.Private: return Strings.CategoryPrivate;
                case NetworkCategory.Public: return Strings.CategoryPublic;
                case NetworkCategory.Domain: return Strings.CategoryDomain;
                default: return Strings.CategoryUnknown;
            }
        }

        private void CopyReport()
        {
            string text = _report.Text;
            if (string.IsNullOrEmpty(text) || _collecting)
            {
                return;
            }
            try
            {
                Clipboard.SetText(text);
                Log.Info("Diagnostics: copied to the clipboard.");
                _shell.Say(Strings.DiagCopied);
            }
            catch (Exception ex)
            {
                Log.Warn("Diagnostics: clipboard copy failed: " + ex.Message);
                _shell.Say(Strings.DiagCopyFailed);
            }
        }

        private void OpenFolder(string folder)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warn("Could not open " + folder + " in Explorer: " + ex.Message);
                _shell.Say(string.Format(Strings.DiagOpenLogsFailed, folder));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _client.SettingsChanged -= OnClientSettingsChanged;
                _updates.Changed -= OnUpdatesChanged;
                _host.SharingChanged -= OnSharingChanged;
            }
            base.Dispose(disposing);
        }
    }
}
