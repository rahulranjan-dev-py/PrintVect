using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrintVect.App.Host;
using PrintVect.App.Theme;
using PrintVect.Core.Config;
using PrintVect.Core.Discovery;
using PrintVect.Core.Host;
using PrintVect.Core.Logging;
using PrintVect.Core.Printing;
using PrintVect.Core.Protocol;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// "Share my printers" (brief, section 7; the Office screen of the design): this PC's name and addresses with the
    /// Sharing switch, the local printers with a Share tick and an editable friendly name, today's printed count and
    /// how another PC connects. The jobs received are on the Jobs page.
    /// </summary>
    internal sealed class ShareTab : UserControl
    {
        private readonly HostController _controller;
        private ToggleSwitch _toggle;
        private ThemedButton _refresh;
        private ThemedButton _pause;
        private ThemedButton _copy;
        private Label _pcName;
        private Label _address;
        private Label _status;
        private Label _hint;
        private Label _printedToday;
        private Label _howSteps;
        private TableLayoutPanel _pcList;
        private Label _pcNone;
        private Timer _seenTimer;
        private DataGridView _grid;
        private DataGridViewCheckBoxColumn _shareColumn;
        private DataGridViewTextBoxColumn _printerColumn;
        private DataGridViewTextBoxColumn _friendlyColumn;
        private DataGridViewTextBoxColumn _statusColumn;
        private string _addressText = "";
        private bool _loading;
        private bool _syncing;

        public ShareTab(HostController controller)
        {
            if (controller == null) throw new ArgumentNullException(nameof(controller));
            _controller = controller;
            BackColor = Palette.Background;
            BuildLayout();

            _controller.SharingChanged += OnSharingChanged;
            _controller.JobChanged += OnJobChanged;
            _controller.ClientsChanged += OnClientsChanged;
            _seenTimer = new Timer { Interval = 60 * 1000 };
            _seenTimer.Tick += (s, e) => FillPcs();   // "connected" turns into "last seen" without any event
            _seenTimer.Start();
            FillPcs();

            ShowAddresses();
            SyncSharingState();
            LoadPrinters();
        }

        /// <summary>Raised with a short text for the status bar ("Copied ...").</summary>
        public event EventHandler<string> Message;

        private void BuildLayout()
        {
            SuspendLayout();
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Ui.Pad(0), BackColor = Palette.Background };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Ui.S(300)));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Card thisPc = CreateThisPcCard();
            layout.Controls.Add(thisPc, 0, 0);
            layout.SetColumnSpan(thisPc, 2);
            layout.Controls.Add(CreatePrintersCard(), 0, 1);
            layout.Controls.Add(CreateSideColumn(), 1, 1);
            Controls.Add(layout);
            ResumeLayout(true);
        }

        private Card CreateThisPcCard()
        {
            var card = new Card { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Ui.Pad(0, 0, 0, 16) };
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                BackColor = Color.Transparent,
                Margin = Ui.Pad(0)
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Left: the PC as others see it.
            Control[] left = new Control[4];
            left[0] = Make.Caption(Strings.ShareThisPcCaption);
            _pcName = Make.Label(Environment.MachineName, ThemeFonts.MonoBig, Palette.Text, Ui.Pad(0, 0, 0, 6));
            _address = Make.Muted("");
            _copy = new ThemedButton(Strings.ShareCopyAddress, ButtonKind.Secondary) { MinimumHeight = 36 };
            _copy.Click += (s, e) => CopyAddress();
            left[1] = _pcName;
            left[2] = _address;
            left[3] = Make.Row(_copy);
            left[3].Margin = Ui.Pad(0);
            table.Controls.Add(Stack(left), 0, 0);

            // Right: the switch that matters most on this screen.
            _toggle = new ToggleSwitch(Strings.ShareToggle) { WrapWidth = 300 };
            _toggle.CheckedChanged += OnToggleChanged;
            _status = Make.Muted("");
            _status.Margin = Ui.Pad(52, 6, 0, 0);
            table.Controls.Add(Stack(Make.Caption(Strings.ShareSharingCaption), _toggle, _status), 1, 0);

            card.Controls.Add(table);
            return card;
        }

        private Card CreatePrintersCard()
        {
            var card = new Card { Dock = DockStyle.Fill, Margin = Ui.Pad(0) };
            TableLayoutPanel column = Make.Column(0, 0, 0, 100);
            _refresh = new ThemedButton(Strings.ShareRefresh, ButtonKind.Secondary) { MinimumHeight = 36, Margin = Ui.Pad(0) };
            _refresh.Click += (s, e) => LoadPrinters();
            _pause = new ThemedButton(Strings.SharePause, ButtonKind.Secondary) { MinimumHeight = 36, Margin = Ui.Pad(0, 0, 8, 0), Enabled = false };
            _pause.Click += (s, e) => TogglePause();
            Label intro = Make.Muted(Strings.ShareIntro);
            _hint = Make.Wrap("", ThemeFonts.Secondary, Palette.Warning, Ui.Pad(0, 0, 0, 6));

            _grid = Grids.Create();
            _grid.ReadOnly = false;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            _shareColumn = Grids.Tick(_grid, Strings.ShareColShare, 12);
            _printerColumn = Grids.Text(Strings.ShareColPrinter, 40);
            _friendlyColumn = Grids.Text(Strings.ShareColFriendly, 33);
            _friendlyColumn.ReadOnly = false;
            _friendlyColumn.DefaultCellStyle = new DataGridViewCellStyle { Font = ThemeFonts.TextSemi };
            _statusColumn = Grids.Text(Strings.ShareColStatus, 15);
            _grid.Columns.AddRange(new DataGridViewColumn[] { _shareColumn, _printerColumn, _friendlyColumn, _statusColumn });
            _grid.CellValueChanged += OnCellValueChanged;
            _grid.CurrentCellChanged += (s, e) => UpdatePauseButton();
            _grid.EditingControlShowing += (s, e) =>
            {
                e.Control.BackColor = Palette.Background;
                e.Control.ForeColor = Palette.Text;
            };
            _grid.DataError += (s, e) =>
            {
                Log.Warn("Printer grid error: " + (e.Exception == null ? "?" : e.Exception.Message));
                e.ThrowException = false;
            };

            FlowLayoutPanel headButtons = Make.Row(_pause, _refresh);
            headButtons.Margin = Ui.Pad(0);
            headButtons.Dock = DockStyle.None;
            headButtons.WrapContents = false;
            column.Controls.Add(Make.Heading(Make.Title(Strings.SharePrintersTitle), headButtons), 0, 0);
            column.Controls.Add(intro, 0, 1);
            column.Controls.Add(_hint, 0, 2);
            column.Controls.Add(_grid, 0, 3);
            card.Controls.Add(column);
            return card;
        }

        private TableLayoutPanel CreateSideColumn()
        {
            TableLayoutPanel side = Make.Column(0, 100);
            side.Margin = Ui.Pad(16, 0, 0, 0);

            var today = new Card { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Ui.Pad(0, 0, 0, 16) };
            _printedToday = Make.Label("0", ThemeFonts.Number, Palette.Text, Ui.Pad(0, 0, 0, 2));
            today.Controls.Add(Stack(Make.Caption(Strings.SharePrintedTodayCaption), _printedToday, Make.Muted(Strings.SharePrintedTodayNote)));

            // The "Computers" card of the design: PCs that said hello or sent a job (M6b-2).
            var pcs = new Card { Dock = DockStyle.Fill, Margin = Ui.Pad(0, 0, 0, 16) };
            _pcList = Make.Column();
            _pcList.AutoSize = true;
            _pcList.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            _pcList.Dock = DockStyle.Top;
            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Ui.Pad(0), BackColor = Palette.Card };
            scroll.Controls.Add(_pcList);
            NativeTheme.UseDarkScrollBars(scroll);
            // No PC yet: the card explains how to connect one instead (the two never need the space at the same time).
            _pcNone = Make.Muted(Strings.SharePcsNone);
            _howSteps = Make.Muted("");
            TableLayoutPanel pcsColumn = Make.Column(0, 0, 0, 100);
            pcsColumn.Controls.Add(Make.Title(Strings.SharePcsTitle), 0, 0);
            pcsColumn.Controls.Add(_pcNone, 0, 1);
            pcsColumn.Controls.Add(_howSteps, 0, 2);
            pcsColumn.Controls.Add(scroll, 0, 3);
            pcs.Controls.Add(pcsColumn);
            pcs.Margin = Ui.Pad(0);

            side.Controls.Add(today, 0, 0);
            side.Controls.Add(pcs, 0, 1);
            return side;
        }

        private void OnClientsChanged(object sender, EventArgs e)
        {
            if (!IsDisposed) FillPcs();
        }

        /// <summary>One row per PC: its name, Windows and PrintVect version, and a pill saying whether it is around.</summary>
        private void FillPcs()
        {
            IList<ClientPresence> clients = _controller.Clients;
            DateTime now = DateTime.Now;
            _pcList.SuspendLayout();
            foreach (Control old in _pcList.Controls.Cast<Control>().ToList())
            {
                old.Dispose();
            }
            _pcList.Controls.Clear();
            _pcList.RowStyles.Clear();
            _pcList.RowCount = clients.Count;
            for (int i = 0; i < clients.Count; i++)
            {
                _pcList.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                _pcList.Controls.Add(PcRow(clients[i], now), 0, i);
            }
            _pcList.ResumeLayout(true);
            _pcNone.Visible = clients.Count == 0;
            _howSteps.Text = clients.Count == 0 ? string.Format(Strings.ShareHowSteps, Environment.MachineName)
                                                : string.Format(Strings.SharePcsAnother, Environment.MachineName);
        }

        private static TableLayoutPanel PcRow(ClientPresence pc, DateTime now)
        {
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 2,
                Margin = Ui.Pad(0, 0, 0, 12),
                BackColor = Palette.Card
            };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            row.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Label name = Make.Label(pc.Name, ThemeFonts.Mono, Palette.Text, Ui.Pad(0, 2, 8, 2));
            bool online = PresenceTracker.IsOnline(pc, now);
            var pill = new StatusPill
            {
                Kind = online ? PillKind.Connected : PillKind.Offline,
                Text = online ? Strings.SharePcOnline : string.Format(Strings.SharePcLastSeen, pc.LastSeen.ToString(
                    pc.LastSeen.Date == now.Date ? "HH:mm" : "dd MMM HH:mm", CultureInfo.CurrentCulture)),
                Anchor = AnchorStyles.Right
            };
            string details = (string.IsNullOrEmpty(pc.Windows) ? Strings.SharePcWindowsUnknown : ShortWindows(pc.Windows))
                             + (string.IsNullOrEmpty(pc.AppVersion) ? "" : " · PrintVect " + pc.AppVersion);
            Label detail = Make.Muted(details);
            detail.Margin = Ui.Pad(0);

            row.Controls.Add(name, 0, 0);
            row.Controls.Add(pill, 1, 0);
            row.Controls.Add(detail, 0, 1);
            row.SetColumnSpan(detail, 2);
            return row;
        }

        /// <summary>"Windows 10 Pro 22H2 (build 19045.4894)" is shown as "Windows 10 Pro 22H2".</summary>
        private static string ShortWindows(string windows)
        {
            int build = windows.IndexOf(" (build", StringComparison.OrdinalIgnoreCase);
            return build > 0 ? windows.Substring(0, build) : windows;
        }

        /// <summary>Controls one under the other, each as tall as its content.</summary>
        private static TableLayoutPanel Stack(params Control[] controls)
        {
            TableLayoutPanel stack = Make.Column(new float[controls.Length]);
            stack.AutoSize = true;
            stack.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            stack.Dock = DockStyle.Top;
            for (int i = 0; i < controls.Length; i++)
            {
                stack.Controls.Add(controls[i], 0, i);
            }
            return stack;
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible && !IsDisposed)
            {
                ShowAddresses();   // a cable plugged in or a new address since the page was last seen
            }
        }

        private void ShowAddresses()
        {
            string[] addresses = LocalNetworks.List().Select(n => n.Address.ToString()).Distinct().ToArray();
            string name = Environment.MachineName;
            _addressText = addresses.Length == 0 ? name : name + " (" + string.Join(", ", addresses) + ")";
            _address.Text = addresses.Length == 0
                ? string.Format(Strings.ShareAddressNone, name)
                : string.Format(Strings.ShareAddressLine, name, string.Join(", ", addresses));
        }

        private void CopyAddress()
        {
            try
            {
                Clipboard.SetText(_addressText);
                Log.Info("Share tab: copied \"" + _addressText + "\" to the clipboard.");
                Message?.Invoke(this, string.Format(Strings.ShareAddressCopied, _addressText));
            }
            catch (Exception ex)
            {
                Log.Warn("Share tab: clipboard copy failed: " + ex.Message);
                Message?.Invoke(this, Strings.DiagCopyFailed);
            }
        }

        public void LoadPrinters()
        {
            if (_loading)
            {
                return;
            }
            _loading = true;
            _refresh.Enabled = false;
            _grid.Rows.Clear();
            ShowHint(Strings.ShareLoading, Palette.Muted);

            _controller.ListPrintersAsync().ContinueWith(task =>
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
                        ShowHint(string.Format(Strings.SharePrintersError, ex.Message), Palette.Warning);
                    }
                    else
                    {
                        FillGrid(task.Result);
                    }
                }
                finally
                {
                    _loading = false;
                    _refresh.Enabled = true;
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private void FillGrid(IList<LocalPrinterInfo> printers)
        {
            _grid.Rows.Clear();
            foreach (LocalPrinterInfo printer in printers)
            {
                SharedPrinter shared = _controller.FindShared(printer.Name);
                int index = _grid.Rows.Add(shared != null, printer.Name, shared == null ? printer.Name : shared.FriendlyName,
                    StatusText(shared != null && shared.Paused ? PrinterStatuses.Paused : printer.Status));
                _grid.Rows[index].Tag = printer.Status;
                MarkPaused(_grid.Rows[index], shared != null && shared.Paused);
            }
            UpdatePauseButton();
            ShowHint(printers.Count == 0 ? Strings.ShareNoPrinters : "", Palette.Warning);
            Log.Info("Share tab: " + printers.Count + " printer(s) listed.");
        }

        /// <summary>The printer of the current row, when it is shared (only a shared printer can be paused).</summary>
        private SharedPrinter CurrentShared()
        {
            DataGridViewRow row = _grid.CurrentRow;
            string printer = row == null ? null : row.Cells[_printerColumn.Index].Value as string;
            return string.IsNullOrEmpty(printer) ? null : _controller.FindShared(printer);
        }

        private void UpdatePauseButton()
        {
            SharedPrinter shared = CurrentShared();
            _pause.Enabled = shared != null;
            _pause.Text = shared != null && shared.Paused ? Strings.ShareResume : Strings.SharePause;
            _pause.Kind = shared != null && shared.Paused ? ButtonKind.Primary : ButtonKind.Secondary;
            _pause.Invalidate();
        }

        private void TogglePause()
        {
            SharedPrinter shared = CurrentShared();
            if (shared == null)
            {
                return;
            }
            bool pause = !shared.Paused;
            if (!_controller.SetPaused(shared.LocalName, pause))
            {
                MessageBox.Show(this, string.Format(Strings.SharingSaveError, _controller.ConfigPath), Strings.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            DataGridViewRow row = _grid.CurrentRow;
            row.Cells[_statusColumn.Index].Value = StatusText(pause ? PrinterStatuses.Paused : row.Tag as string);
            MarkPaused(row, pause);
            UpdatePauseButton();
        }

        /// <summary>A paused printer's status is shown in yellow, like everything that waits for someone.</summary>
        private void MarkPaused(DataGridViewRow row, bool paused)
        {
            DataGridViewCell status = row.Cells[_statusColumn.Index];
            status.Style.ForeColor = paused ? Palette.Warning : Palette.Text;
            status.Style.SelectionForeColor = status.Style.ForeColor;
        }

        /// <summary>The line above the printer list: loading, no printers, or why the list could not be read.</summary>
        private void ShowHint(string text, Color colour)
        {
            _hint.ForeColor = colour;
            _hint.Text = text;
            _hint.Visible = text.Length > 0;
        }

        private void OnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_loading || e.RowIndex < 0)
            {
                return;
            }

            DataGridViewRow row = _grid.Rows[e.RowIndex];
            string printer = row.Cells[_printerColumn.Index].Value as string;
            if (string.IsNullOrEmpty(printer))
            {
                return;
            }

            string friendly = ((row.Cells[_friendlyColumn.Index].Value as string) ?? "").Trim();
            if (friendly.Length == 0)
            {
                friendly = printer;
                _loading = true;
                row.Cells[_friendlyColumn.Index].Value = friendly;
                _loading = false;
            }
            bool shared = row.Cells[_shareColumn.Index].Value is bool ticked && ticked;

            bool saved;
            if (e.ColumnIndex == _shareColumn.Index)
            {
                saved = _controller.SetShared(printer, shared, friendly);
                row.Cells[_statusColumn.Index].Value = StatusText(row.Tag as string);
                MarkPaused(row, false);
                UpdatePauseButton();
            }
            else if (e.ColumnIndex == _friendlyColumn.Index)
            {
                saved = !shared || _controller.SetFriendlyName(printer, friendly);
            }
            else
            {
                return;
            }

            if (!saved)
            {
                MessageBox.Show(this, string.Format(Strings.SharingSaveError, _controller.ConfigPath), Strings.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnToggleChanged(object sender, EventArgs e)
        {
            if (_syncing)
            {
                return;
            }
            string error;
            if (!_controller.TrySetSharing(_toggle.Checked, out error))
            {
                _syncing = true;
                _toggle.Checked = _controller.IsSharing;
                _syncing = false;
                MessageBox.Show(this, error, Strings.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            SyncSharingState();
        }

        private void OnSharingChanged(object sender, EventArgs e)
        {
            if (!IsDisposed)
            {
                SyncSharingState();
            }
        }

        private void SyncSharingState()
        {
            _syncing = true;
            _toggle.Checked = _controller.IsSharing;
            _syncing = false;
            _status.Text = _controller.IsSharing ? string.Format(Strings.ShareStatusOn, _controller.Port) : Strings.ShareStatusOff;
            UpdateJobCount();
        }

        private void OnJobChanged(object sender, JobRecord record)
        {
            if (!IsDisposed)
            {
                UpdateJobCount();
            }
        }

        private void UpdateJobCount()
        {
            _printedToday.Text = _controller.Jobs.CountToday(JobStates.Printed).ToString(CultureInfo.CurrentCulture);
        }

        internal static string StatusText(string status)
        {
            return Lookup("PrinterStatus_", status);
        }

        internal static string StateText(string state)
        {
            return Lookup("JobState_", state);
        }

        private static string Lookup(string prefix, string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return "";
            }
            string text = Strings.ResourceManager.GetString(prefix + key.Replace(' ', '_'), Strings.Culture);
            return string.IsNullOrEmpty(text) ? key : text;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _controller.SharingChanged -= OnSharingChanged;
                _controller.JobChanged -= OnJobChanged;
                _controller.ClientsChanged -= OnClientsChanged;
                _seenTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
