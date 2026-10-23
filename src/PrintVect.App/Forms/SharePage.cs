using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrintVect.App.Client;
using PrintVect.App.Host;
using PrintVect.App.Theme;
using PrintVect.Core.Config;
using PrintVect.Core.Diagnostics;
using PrintVect.Core.Elevation;
using PrintVect.Core.Host;
using PrintVect.Core.Logging;
using PrintVect.Core.Printing;
using PrintVect.Core.Protocol;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// "Share my printers" in the redesign of 0.5.0: the sharing card (Turn on sharing opens a window to pick the printers
    /// and the PIN), the name other PCs find this PC by, a warning when there is no office PIN or when Windows treats the
    /// network as Public (the hotspot fix: Make it private), the printers of this PC with a tick, the name other PCs see,
    /// the status and Pause / Resume, and on the right the jobs printed today, the PCs that print here and how they connect.
    /// PrintVect's own printers (printers of other PCs) are not listed: sharing one again would send jobs round in a loop.
    /// </summary>
    internal sealed class SharePage : PageScroller, IPage
    {
        private static readonly TimeSpan NetworkCheckInterval = TimeSpan.FromSeconds(30);

        private readonly AppPaths _paths;
        private readonly ConfigStore _store;
        private readonly AppConfig _config;
        private readonly HostController _host;
        private readonly ClientController _client;
        private readonly IShell _shell;

        private readonly StatusPill _sharingPill;
        private readonly Label _sharingText;
        private readonly ThemedButton _turnOn;
        private readonly ThemedButton _turnOff;
        private readonly Label _addresses;
        private readonly Notice _pinNotice;
        private readonly Notice _publicNotice;
        private readonly ThemedButton _makePrivate;
        private readonly TableLayoutPanel _printerRows;
        private readonly Notice _notListed;
        private readonly ThemedButton _refresh;
        private readonly Label _printedToday;
        private readonly TableLayoutPanel _pcRows;
        private readonly Timer _timer;
        private readonly ToolTip _tips = new ToolTip();
        private IList<NetworkConnection> _publicNetworks = new List<NetworkConnection>();
        private DateTime _networksCheckedAt = DateTime.MinValue;
        private bool _loading;
        private bool _checkingNetworks;

        public SharePage(AppPaths paths, ConfigStore store, AppConfig config, HostController host, ClientController client, IShell shell)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            _paths = paths;
            _store = store;
            _config = config;
            _host = host;
            _client = client;
            _shell = shell;

            Add(Make.Stack(6, Make.PageTitle(Strings.TabShare), Make.Intro(Strings.ShareIntro)), 0);

            // The sharing card: on or off, how other PCs find this one, and what still needs doing.
            _sharingPill = new StatusPill { Anchor = AnchorStyles.Left, Margin = Ui.Pad(10, 0, 0, 0) };
            Label title = Make.Label(Strings.ShareWithOffice, ThemeFonts.Heading, Palette.Text, Ui.Pad(0));
            title.Anchor = AnchorStyles.Left;
            _sharingText = Make.Intro("");
            _turnOn = new ThemedButton(Strings.ShareTurnOn, ButtonKind.Primary) { Margin = Ui.Pad(0) };
            _turnOn.Click += (s, e) => TurnOn();
            _turnOff = new ThemedButton(Strings.ShareTurnOff, ButtonKind.Secondary) { Margin = Ui.Pad(0) };
            _turnOff.Click += (s, e) => TurnOff();
            FlowLayoutPanel switchButtons = Make.Row(_turnOn, _turnOff);
            switchButtons.WrapContents = false;
            var sharingRow = new SplitRow(null, Make.Stack(6, Make.Row(title, _sharingPill), _sharingText), switchButtons, 260, 16);

            Label findAs = Make.Label(Strings.ShareFindAs, ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0));
            Label name = Make.Label(Environment.MachineName, ThemeFonts.MonoBig, Palette.Text, Ui.Pad(0));
            _addresses = Make.Wrap("", ThemeFonts.Secondary, Palette.Faint, Ui.Pad(0));
            var copy = new ThemedButton(Strings.ShareCopyName, ButtonKind.Secondary, Glyph.Copy) { Margin = Ui.Pad(0) };
            copy.Click += (s, e) => CopyName();
            var nameRow = new SplitRow(null, Make.Stack(4, findAs, name, _addresses), copy, 240, 16);

            _pinNotice = new Notice(NoticeKind.Warning, Glyph.Lock, "", Make.Link(Strings.SharePinLink, Palette.WarningText, () => _shell.Go(PageId.Settings)));
            _makePrivate = new ThemedButton(Strings.ShareMakePrivate, ButtonKind.Secondary, Glyph.Network) { Margin = Ui.Pad(0) };
            _makePrivate.Click += (s, e) => MakePrivate();
            _publicNotice = new Notice(NoticeKind.Warning, Glyph.Alert, "", _makePrivate);

            var card = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Margin = Ui.Pad(0), BackColor = Color.Transparent };
            card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(card, sharingRow, Ui.Pad(22, 14, 22, 20));
            AddRow(card, Make.Divider(), Ui.Pad(0));
            AddRow(card, nameRow, Ui.Pad(22, 16, 22, 16));
            AddRow(card, _pinNotice, Ui.Pad(22, 0, 22, 16));
            AddRow(card, _publicNotice, Ui.Pad(22, 0, 22, 14));
            Add(Make.Card(card, 0, 0), 24);

            // Left: the printers of this PC. Right: who prints here.
            Label listTitle = Make.SectionTitle(Strings.SharePrintersLocal);
            listTitle.Anchor = AnchorStyles.Left;
            Label tickHint = Make.Label(Strings.ShareTickHint, ThemeFonts.Secondary, Palette.Faint, Ui.Pad(0, 0, 4, 0));
            tickHint.Anchor = AnchorStyles.Left;
            _refresh = new ThemedButton(Strings.ShareRefresh, ButtonKind.Ghost, Glyph.Retry) { IconOnly = true, Compact = true, Margin = Ui.Pad(0) };
            _refresh.AccessibleName = Strings.ShareRefreshList;
            _tips.SetToolTip(_refresh, Strings.ShareRefreshList);
            _refresh.Click += (s, e) => LoadPrinters();
            FlowLayoutPanel listRight = Make.Row(tickHint, _refresh);
            listRight.WrapContents = false;
            _printerRows = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Margin = Ui.Pad(0), BackColor = Color.Transparent };
            _printerRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _notListed = new Notice(NoticeKind.Plain, Glyph.Info, "");
            // The list's header strip draws the card's round top corners itself, so the list starts 1 px from the top.
            var listCard = new Card { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(1, 1, 1, Ui.S(6)) };
            _printerRows.Dock = DockStyle.Fill;
            listCard.Controls.Add(_printerRows);
            TableLayoutPanel left = Make.Stack(12, Make.Heading(listTitle, listRight), listCard, _notListed);

            _printedToday = Make.Label("0", ThemeFonts.Number, Palette.Text, Ui.Pad(0, 0, 6, 0));
            _printedToday.Anchor = AnchorStyles.Left;
            Label jobsNote = Make.Label(Strings.SharePrintedTodayNote, ThemeFonts.Text, Palette.Muted, Ui.Pad(0, 8, 0, 0));
            jobsNote.Anchor = AnchorStyles.Left;
            _pcRows = Make.Stack(10);
            TableLayoutPanel today = Make.Stack(14,
                Make.Stack(2, Make.Label(Strings.SharePrintedHere, ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0)), Make.Row(_printedToday, jobsNote)),
                Make.Divider(),
                Make.Stack(6, Make.Label(Strings.SharePcsTitle, ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0)), _pcRows));
            TableLayoutPanel how = Make.Stack(10, Make.Label(Strings.ShareHowTitle, ThemeFonts.TextSemi, Palette.Text, Ui.Pad(0)),
                Step(1, Strings.ShareHow1), Step(2, Strings.ShareHow2), Step(3, string.Format(Strings.ShareHow3, Environment.MachineName)));
            TableLayoutPanel right = Make.Stack(16, Make.Card(today, 20, 18), Make.Card(how, 20, 18));
            Add(new TwoColumns(left, right, 520, 999, 260, 1, 24), 24);

            _timer = new Timer { Interval = 30 * 1000 };
            _timer.Tick += (s, e) => OnTimer();

            _host.SharingChanged += OnSharingChanged;
            _host.JobChanged += OnJobChanged;
            _host.ClientsChanged += OnClientsChanged;
            _client.PrintersChanged += OnClientPrintersChanged;
            NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

            ShowAddresses();
            SyncSharing();
            FillPcs();
            ShowNotListed();
            LoadPrinters();
        }

        private static void AddRow(TableLayoutPanel table, Control control, Padding margin)
        {
            control.Dock = DockStyle.Fill;
            control.Margin = margin;
            table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(control, 0, table.RowCount - 1);
        }

        private static Control Step(int number, string text)
        {
            Label n = Make.Label(number.ToString(CultureInfo.CurrentCulture) + ".", ThemeFonts.Text, Palette.TextSecondary, Ui.Pad(0));
            return new SplitRow(n, Make.Wrap(text, ThemeFonts.Text, Palette.TextSecondary, Ui.Pad(0)), null, 120, 8) { AlignTop = true };
        }

        public void SetOpen(bool open)
        {
            if (open && !IsDisposed)
            {
                ShowAddresses();   // a cable plugged in or a new address since the page was last seen
                CheckNetworks();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        }

        private void OnTimer()
        {
            FillPcs();   // "Connected" turns into "Last seen" without any event
            if (DateTime.Now - _networksCheckedAt >= NetworkCheckInterval) CheckNetworks();
        }

        // ---- Sharing on / off ----

        private void SyncSharing()
        {
            bool on = _host.IsSharing;
            _sharingPill.Set(on ? Strings.ShareOnPill : Strings.ShareOffPill, on ? PillKind.Success : PillKind.Neutral);
            _sharingText.Text = on ? Strings.ShareOnText : Strings.ShareOffText;
            _turnOn.Visible = !on;
            _turnOff.Visible = on;
            bool noPin = string.IsNullOrEmpty(_config.Pin);
            _pinNotice.Message = on ? Strings.SharePinNoticeOn : Strings.SharePinNoticeOff;
            _pinNotice.Visible = noPin;
            ShowPublicNotice();
            UpdateJobCount();
        }

        private void TurnOn()
        {
            using (var dialog = new SharingDialog(_host, !string.IsNullOrEmpty(_config.Pin)))
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
                {
                    return;
                }
                bool saved = true;
                foreach (KeyValuePair<string, bool> choice in dialog.Choices)
                {
                    SharedPrinter existing = _host.FindShared(choice.Key);
                    if (choice.Value == (existing != null)) continue;
                    saved &= _host.SetShared(choice.Key, choice.Value, existing == null ? choice.Key : existing.FriendlyName);
                }
                if (!string.IsNullOrEmpty(dialog.Pin))
                {
                    saved &= PinSetting.Save(_store, _config, _host, _client, dialog.Pin);
                }
                if (!saved) SaveFailed();
            }
            SetSharing(true);
            LoadPrinters();
        }

        private void TurnOff()
        {
            SetSharing(false);
        }

        private void SetSharing(bool on)
        {
            string error;
            if (!_host.TrySetSharing(on, out error))
            {
                MessageBox.Show(FindForm(), error, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            SyncSharing();
        }

        private void OnSharingChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) SyncSharing();
        }

        /// <summary>Windows can take a while to list the network cards (several cards, a phone on USB): not on the UI thread.</summary>
        private void ShowAddresses()
        {
            Task.Run(() => NetworkCards()).ContinueWith(task =>
            {
                if (IsDisposed) return;
                if (task.IsFaulted)
                {
                    Log.Warn("Share page: the network cards could not be listed: " + (task.Exception == null ? "?" : task.Exception.GetBaseException().Message));
                    return;
                }
                List<string> parts = task.Result.Select(c => c.Key + " " + string.Join(", ", c.Value)).ToList();
                string text = parts.Count == 0 ? Strings.ShareNoAddress : string.Join(" · ", parts);
                if (_addresses.Text != text) _addresses.Text = text;
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Each network card that is up with its IPv4 addresses ("Ethernet" 192.168.1.20).</summary>
        private static List<KeyValuePair<string, List<string>>> NetworkCards()
        {
            return Core.Discovery.LocalNetworks.List()
                .GroupBy(n => n.InterfaceName, StringComparer.OrdinalIgnoreCase)
                .Select(g => new KeyValuePair<string, List<string>>(g.Key, g.Select(n => n.Address.ToString()).Distinct().ToList()))
                .ToList();
        }

        private void CopyName()
        {
            string name = Environment.MachineName;
            try
            {
                Clipboard.SetText(name);
                Log.Info("Share page: copied \"" + name + "\" to the clipboard.");
                _shell.Say(string.Format(Strings.ShareAddressCopied, name));
            }
            catch (Exception ex)
            {
                Log.Warn("Share page: clipboard copy failed: " + ex.Message);
                _shell.Say(Strings.DiagCopyFailed);
            }
        }

        // ---- Public networks (the hotspot fix) ----

        private void OnNetworkAddressChanged(object sender, EventArgs e)
        {
            // Raised on a thread-pool thread when a card connects, disconnects or gets a new address.
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke((Action)(() =>
                {
                    if (IsDisposed) return;
                    ShowAddresses();
                    CheckNetworks();
                }));
            }
            catch (InvalidOperationException)
            {
                // The page was closed in between: nothing to update.
            }
        }

        /// <summary>Asks Windows on a background thread which networks are Public; the warning shows while sharing is on.</summary>
        private void CheckNetworks()
        {
            if (_checkingNetworks || IsDisposed) return;
            _checkingNetworks = true;
            _networksCheckedAt = DateTime.Now;
            Task.Run(() => NetworkProfiles.PublicNetworks(NetworkProfiles.Connected())).ContinueWith(task =>
            {
                _checkingNetworks = false;
                if (IsDisposed) return;
                if (task.IsFaulted)
                {
                    Log.Warn("Network categories could not be read: " + (task.Exception == null ? "?" : task.Exception.GetBaseException().Message));
                    return;
                }
                IList<NetworkConnection> found = task.Result;
                bool changed = found.Count != _publicNetworks.Count || found.Any(f => _publicNetworks.All(p => p.NetworkId != f.NetworkId));
                if (changed && found.Count > 0)
                {
                    Log.Info("Public networks: " + string.Join(", ", found.Select(f => "\"" + f.Network + "\" on " + f.Adapter)) + ".");
                }
                _publicNetworks = found;
                ShowPublicNotice();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void ShowPublicNotice()
        {
            bool show = _host.IsSharing && _publicNetworks.Count > 0;
            if (show)
            {
                string names = string.Join(" " + Strings.WordAnd + " ", _publicNetworks.Select(n => "“" + n.Network + "”"));
                _publicNotice.Message = string.Format(_publicNetworks.Count == 1 ? Strings.SharePublicOne : Strings.SharePublicMany, names);
            }
            _publicNotice.Visible = show;
        }

        /// <summary>Make it private: PrintVect.Elevate asks Windows (one permission question) to treat these networks as Private.</summary>
        private async void MakePrivate()
        {
            if (_publicNetworks.Count == 0) return;
            var arguments = new ElevateArguments { Command = ElevateCommands.NetworkPrivate };
            arguments.Options[ElevateCommands.OptionNetworks] = string.Join(",", _publicNetworks.Select(n => n.NetworkId.ToString("D")));
            _makePrivate.Enabled = false;
            _shell.Say(Strings.ShareMakingPrivate);
            Log.Info("Make it private: " + string.Join(", ", _publicNetworks.Select(n => "\"" + n.Network + "\" (" + n.NetworkId + ")")) + ".");
            try
            {
                ElevateResult result = await ElevateLauncher.RunAsync(_paths, arguments);
                if (IsDisposed) return;
                if (result.Ok)
                {
                    _shell.Say(result.Message);
                }
                else
                {
                    MessageBox.Show(FindForm(), string.Format(Strings.SharePrivateFailed, result.Message), Strings.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Making the network private failed.", ex);
                if (!IsDisposed) _shell.Say(string.Format(Strings.SharePrivateFailed, ex.Message));
            }
            finally
            {
                if (!IsDisposed)
                {
                    _makePrivate.Enabled = true;
                    CheckNetworks();
                }
            }
        }

        // ---- The printers of this PC ----

        public void LoadPrinters()
        {
            if (_loading)
            {
                return;
            }
            _loading = true;
            _refresh.Enabled = false;
            FillPrinters(null, Strings.ShareLoading);

            _host.ListPrintersAsync().ContinueWith(task =>
            {
                if (IsDisposed)
                {
                    return;
                }
                try
                {
                    if (task.IsFaulted)
                    {
                        Exception ex = task.Exception == null ? new Exception("?") : task.Exception.GetBaseException();
                        Log.Error("The printer list could not be read.", ex);
                        FillPrinters(null, string.Format(Strings.SharePrintersError, ex.Message));
                    }
                    else
                    {
                        IList<LocalPrinterInfo> printers = task.Result;
                        FillPrinters(printers, printers.Count == 0 ? Strings.ShareNoPrinters : null);
                        Log.Info("Share page: " + printers.Count + " printer(s) listed.");
                    }
                }
                finally
                {
                    _loading = false;
                    _refresh.Enabled = true;
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void FillPrinters(IList<LocalPrinterInfo> printers, string message)
        {
            _printerRows.SuspendLayout();
            foreach (Control old in _printerRows.Controls.Cast<Control>().ToList()) old.Dispose();
            _printerRows.Controls.Clear();
            _printerRows.RowStyles.Clear();
            _printerRows.RowCount = 0;
            AddRow(_printerRows, new PrinterRow(null, null, true, false), Ui.Pad(0));
            if (printers != null)
            {
                for (int i = 0; i < printers.Count; i++)
                {
                    AddRow(_printerRows, new PrinterRow(this, printers[i], false, i == 0), Ui.Pad(0));
                }
            }
            if (message != null)
            {
                AddRow(_printerRows, Make.Muted(message), Ui.Pad(20, 14, 20, 16));
            }
            Ui.Solidify(_printerRows);
            _printerRows.ResumeLayout(true);
        }

        /// <summary>"Dispatch and Supervisor are not listed..." with the names of the printers added from other PCs.</summary>
        private void ShowNotListed()
        {
            List<string> names = _client.Printers.Select(p => string.IsNullOrWhiteSpace(p.FriendlyName) ? p.LocalPrinterName : p.FriendlyName)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string list = names.Count <= 1 ? string.Join("", names)
                : string.Join(", ", names.Take(names.Count - 1)) + " " + Strings.WordAnd + " " + names[names.Count - 1];
            string first = names.Count == 0 ? "" : string.Format(names.Count == 1 ? Strings.ShareNotListedOne : Strings.ShareNotListedMany, list) + " ";
            _notListed.Message = first + Strings.ShareNameHint;
        }

        private void OnClientPrintersChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) ShowNotListed();
        }

        private void SaveFailed()
        {
            MessageBox.Show(FindForm(), string.Format(Strings.SharingSaveError, _host.ConfigPath), Strings.AppName,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        /// <summary>One printer of this PC: tick, name and kind, the name other PCs see, status, Pause / Resume.</summary>
        private sealed class PrinterRow : TableLayoutPanel
        {
            private readonly SharePage _page;
            private readonly LocalPrinterInfo _printer;
            private readonly bool _header;
            private readonly bool _first;
            private readonly TickBox _tick;
            private readonly InputBox _name;
            private readonly StatusPill _status;
            private readonly ThemedButton _pause;

            public PrinterRow(SharePage page, LocalPrinterInfo printer, bool header, bool first)
            {
                _page = page;
                _printer = printer;
                _header = header;
                _first = first;
                ColumnCount = 4;
                RowCount = 1;
                AutoSize = true;
                Margin = Ui.Pad(0);
                Padding = header ? Ui.Pad(20, 11, 20, 11) : Ui.Pad(20, 10, 20, 10);
                BackColor = header ? Palette.HeaderBg : Palette.Card;
                SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
                ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(64)));
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
                ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(196)));
                RowStyles.Add(new RowStyle(SizeType.AutoSize));

                if (header)
                {
                    Controls.Add(HeaderText(Strings.ShareColShare), 0, 0);
                    Controls.Add(HeaderText(Strings.ShareColPrinter), 1, 0);
                    Controls.Add(HeaderText(Strings.ShareColName), 2, 0);
                    Controls.Add(HeaderText(Strings.ShareColStatus), 3, 0);
                    return;
                }

                SharedPrinter shared = page._host.FindShared(printer.Name);
                _tick = new TickBox { Checked = shared != null, Anchor = AnchorStyles.Left, AccessibleName = string.Format(Strings.ShareTickAccessible, printer.Name) };
                _tick.CheckedChanged += (s, e) => OnTicked();
                Label name = Make.Label(printer.Name, ThemeFonts.TextSemi, Palette.Text, Ui.Pad(0));
                string kind = Texts.PrinterKind(printer);
                var kindLabel = new Label
                {
                    Text = kind, Font = ThemeFonts.Small, ForeColor = Palette.Faint, AutoSize = false, AutoEllipsis = true, UseMnemonic = false,
                    Height = TextRenderer.MeasureText("Ag", ThemeFonts.Small).Height, Margin = Ui.Pad(0)
                };
                Control nameCell = kind.Length == 0 ? (Control)name : Make.Stack(2, name, kindLabel);
                nameCell.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                nameCell.Dock = DockStyle.None;
                nameCell.Margin = Ui.Pad(0, 0, 14, 0);
                _name = new InputBox(220, 38, ThemeFonts.Text) { Soft = true, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = Ui.Pad(0, 0, 14, 0) };
                _name.Box.Text = shared == null ? printer.Name : shared.FriendlyName;
                _name.Box.AccessibleName = string.Format(Strings.ShareNameAccessible, printer.Name);
                _name.Box.Leave += (s, e) => SaveName();
                _name.Box.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Enter)
                    {
                        e.SuppressKeyPress = true;
                        SaveName();
                    }
                };
                _status = new StatusPill { Anchor = AnchorStyles.Left, Margin = Ui.Pad(0, 0, 10, 0) };
                _pause = new ThemedButton(Strings.SharePauseShort, ButtonKind.Ghost, Glyph.Pause) { Compact = true, Margin = Ui.Pad(0) };
                _pause.Click += (s, e) => TogglePause();
                FlowLayoutPanel statusCell = Make.Row(_status, _pause);
                statusCell.WrapContents = false;
                statusCell.Anchor = AnchorStyles.Left;

                Controls.Add(_tick, 0, 0);
                Controls.Add(nameCell, 1, 0);
                Controls.Add(_name, 2, 0);
                Controls.Add(statusCell, 3, 0);
                ShowStatus();
            }

            private static Label HeaderText(string text)
            {
                Label label = Make.Label(text, ThemeFonts.Small, Palette.Muted, Ui.Pad(0));
                label.Anchor = AnchorStyles.Left;
                return label;
            }

            private void ShowStatus()
            {
                SharedPrinter shared = _page._host.FindShared(_printer.Name);
                bool paused = shared != null && shared.Paused;
                string status = paused ? PrinterStatuses.Paused : _printer.Status;
                _status.Set(Texts.PrinterStatus(status), Texts.PrinterPill(status));
                _pause.Visible = shared != null;
                _pause.Text = paused ? Strings.ShareResumeShort : Strings.SharePauseShort;
                _pause.Glyph = paused ? Glyph.Play : Glyph.Pause;
                _pause.AccessibleName = string.Format(paused ? Strings.ShareResumeAccessible : Strings.SharePauseAccessible, _printer.Name);
            }

            private void OnTicked()
            {
                string friendly = (_name.Box.Text ?? "").Trim();
                if (friendly.Length == 0) friendly = _printer.Name;
                if (!_page._host.SetShared(_printer.Name, _tick.Checked, friendly)) _page.SaveFailed();
                ShowStatus();
            }

            private void SaveName()
            {
                string friendly = (_name.Box.Text ?? "").Trim();
                if (friendly.Length == 0)
                {
                    friendly = _printer.Name;
                    _name.Box.Text = friendly;
                }
                SharedPrinter shared = _page._host.FindShared(_printer.Name);
                if (shared == null || shared.FriendlyName == friendly) return;
                if (!_page._host.SetFriendlyName(_printer.Name, friendly)) _page.SaveFailed();
            }

            private void TogglePause()
            {
                SharedPrinter shared = _page._host.FindShared(_printer.Name);
                if (shared == null) return;
                if (!_page._host.SetPaused(shared.LocalName, !shared.Paused)) _page.SaveFailed();
                ShowStatus();
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                if (!_header)
                {
                    base.OnPaintBackground(e);
                    if (!_first)
                    {
                        using (var pen = new Pen(Palette.RowDivider))
                        {
                            e.Graphics.DrawLine(pen, 0, 0, Width, 0);
                        }
                    }
                    return;
                }
                // The header strip has the card's round top corners and a line under it.
                e.Graphics.Clear(Palette.Card);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float r = Ui.S(10);
                using (var path = new GraphicsPath())
                {
                    path.AddArc(1, 1, r * 2 - 2, r * 2 - 2, 180, 90);
                    path.AddArc(Width - r * 2, 1, r * 2 - 2, r * 2 - 2, 270, 90);
                    path.AddLine(Width - 1, r, Width - 1, Height);
                    path.AddLine(Width - 1, Height, 1, Height);
                    path.CloseFigure();
                    using (var brush = new SolidBrush(Palette.HeaderBg)) e.Graphics.FillPath(brush, path);
                }
                using (var pen = new Pen(Palette.Divider))
                {
                    e.Graphics.DrawLine(pen, 0, Height - 1, Width, Height - 1);
                }
            }
        }

        // ---- Who prints here ----

        private void OnClientsChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) FillPcs();
        }

        private void OnJobChanged(object sender, JobRecord record)
        {
            if (!IsDisposed) UpdateJobCount();
        }

        private void UpdateJobCount()
        {
            _printedToday.Text = _host.Jobs.CountToday(JobStates.Printed).ToString(CultureInfo.CurrentCulture);
        }

        /// <summary>One line per PC: its name, Windows and PrintVect version, and a pill saying whether it is around.</summary>
        private void FillPcs()
        {
            IList<ClientPresence> clients = _host.Clients;
            DateTime now = DateTime.Now;
            _pcRows.SuspendLayout();
            foreach (Control old in _pcRows.Controls.Cast<Control>().ToList()) old.Dispose();
            _pcRows.Controls.Clear();
            _pcRows.RowStyles.Clear();
            _pcRows.RowCount = 0;
            if (clients.Count == 0)
            {
                AddRow(_pcRows, Make.Label(Strings.SharePcsNoneYet, ThemeFonts.TextSemi, Palette.Text, Ui.Pad(0)), Ui.Pad(0));
            }
            foreach (ClientPresence pc in clients)
            {
                bool online = PresenceTracker.IsOnline(pc, now);
                var pill = new StatusPill(online ? Strings.SharePcOnline : string.Format(Strings.SharePcLastSeen, Texts.When(pc.LastSeen)),
                    online ? PillKind.Success : PillKind.Neutral);
                string details = (string.IsNullOrEmpty(pc.Windows) ? Strings.SharePcWindowsUnknown : ShortWindows(pc.Windows))
                                 + (string.IsNullOrEmpty(pc.AppVersion) ? "" : " · PrintVect " + pc.AppVersion);
                TableLayoutPanel text = Make.Stack(2, Make.Label(pc.Name, ThemeFonts.MonoSemi, Palette.Text, Ui.Pad(0)),
                    Make.Wrap(details, ThemeFonts.Small, Palette.Faint, Ui.Pad(0)));
                AddRow(_pcRows, new SplitRow(null, text, pill, 120, 8) { AlignTop = true }, Ui.Pad(0, _pcRows.RowCount == 0 ? 0 : 10, 0, 0));
            }
            Ui.Solidify(_pcRows);
            _pcRows.ResumeLayout(true);
        }

        /// <summary>"Windows 10 Pro 22H2 (build 19045.4894)" is shown as "Windows 10 Pro 22H2".</summary>
        private static string ShortWindows(string windows)
        {
            int build = windows.IndexOf(" (build", StringComparison.OrdinalIgnoreCase);
            return build > 0 ? windows.Substring(0, build) : windows;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
                _host.SharingChanged -= OnSharingChanged;
                _host.JobChanged -= OnJobChanged;
                _host.ClientsChanged -= OnClientsChanged;
                _client.PrintersChanged -= OnClientPrintersChanged;
                _timer.Dispose();
                _tips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
