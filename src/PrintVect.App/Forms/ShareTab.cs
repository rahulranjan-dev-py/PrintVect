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
        private ThemedButton _copy;
        private Label _pcName;
        private Label _address;
        private Label _status;
        private Label _hint;
        private Label _printedToday;
        private Label _howSteps;
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
            Label intro = Make.Muted(Strings.ShareIntro);
            _hint = Make.Wrap("", ThemeFonts.Secondary, Palette.Warning, Ui.Pad(0, 0, 0, 6));

            _grid = Grids.Create();
            _grid.ReadOnly = false;
            _grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            _grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            _shareColumn = Grids.Tick(Strings.ShareColShare, 12);
            _shareColumn.MinimumWidth = Ui.S(56);
            _printerColumn = Grids.Text(Strings.ShareColPrinter, 40);
            _friendlyColumn = Grids.Text(Strings.ShareColFriendly, 33);
            _friendlyColumn.ReadOnly = false;
            _friendlyColumn.DefaultCellStyle = new DataGridViewCellStyle { Font = ThemeFonts.TextSemi };
            _statusColumn = Grids.Text(Strings.ShareColStatus, 15);
            _grid.Columns.AddRange(new DataGridViewColumn[] { _shareColumn, _printerColumn, _friendlyColumn, _statusColumn });
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewCheckBoxCell)
                {
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                }
            };
            _grid.CellValueChanged += OnCellValueChanged;
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

            column.Controls.Add(Make.Heading(Make.Title(Strings.SharePrintersTitle), _refresh), 0, 0);
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

            var how = new Card { Dock = DockStyle.Fill, Margin = Ui.Pad(0) };
            _howSteps = Make.Muted("");
            TableLayoutPanel howColumn = Make.Column(0, 100);
            howColumn.Controls.Add(Make.Title(Strings.ShareHowTitle), 0, 0);
            howColumn.Controls.Add(_howSteps, 0, 1);
            how.Controls.Add(howColumn);

            side.Controls.Add(today, 0, 0);
            side.Controls.Add(how, 0, 1);
            return side;
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
            _howSteps.Text = string.Format(Strings.ShareHowSteps, name);
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
                _grid.Rows.Add(shared != null, printer.Name, shared == null ? printer.Name : shared.FriendlyName, StatusText(printer.Status));
            }
            ShowHint(printers.Count == 0 ? Strings.ShareNoPrinters : "", Palette.Warning);
            Log.Info("Share tab: " + printers.Count + " printer(s) listed.");
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
            }
            base.Dispose(disposing);
        }
    }
}
