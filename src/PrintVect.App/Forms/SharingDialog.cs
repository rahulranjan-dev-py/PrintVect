using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PrintVect.App.Host;
using PrintVect.App.Theme;
using PrintVect.Core.Logging;
using PrintVect.Core.Printing;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// "Share printers with the office?" (0.5.0, from the owner's redesign): which printers to share and, when this PC has
    /// none yet, the office PIN, in one step before sharing is turned on. The same PIN rule as Settings: 4 to 12
    /// characters, typed twice, the same on every PC; blank shares without a PIN.
    /// </summary>
    internal sealed class SharingDialog : Form
    {
        private const int MaxListHeightPx = 236;

        private readonly HostController _host;
        private readonly bool _hasPin;
        private readonly TableLayoutPanel _printers;
        private readonly Panel _printerScroll;
        private readonly Label _loading;
        private readonly InputBox _pin;
        private readonly InputBox _again;
        private readonly Label _problem;
        private readonly ThemedButton _ok;
        private readonly List<TickBox> _ticks = new List<TickBox>();
        private readonly TableLayoutPanel _content;

        public SharingDialog(HostController host, bool hasPin)
        {
            _host = host;
            _hasPin = hasPin;
            AutoScaleMode = AutoScaleMode.None;
            Text = Strings.SharingDialogTitle;
            Icon = AppIcon.Window;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Palette.Card;
            ForeColor = Palette.Text;
            Font = ThemeFonts.Text;
            ClientSize = new Size(Ui.S(560), Ui.S(560));

            var icon = new IconTile(Glyph.Share, 42, 22, Palette.SuccessBg, Palette.Accent, false, 10);
            var heading = new SplitRow(icon, Make.Stack(4, Make.Label(Strings.SharingDialogHeading, ThemeFonts.DialogTitle, Palette.Text, Ui.Pad(0)),
                Make.Intro(Strings.SharingDialogText)), null, 200, 14) { AlignTop = true };

            _printers = Make.Stack(6);
            _loading = Make.Muted(Strings.ShareLoading);
            _printerScroll = new Panel { AutoScroll = true, Height = Ui.S(60), Margin = Ui.Pad(0), BackColor = Palette.Card };
            _printers.Dock = DockStyle.Top;
            _printerScroll.Controls.Add(_printers);
            NativeTheme.UseThemedScrollBars(_printerScroll);
            Control printers = Make.Stack(8, Make.Label(Strings.SharingDialogPrinters, ThemeFonts.SecondarySemi, Palette.Text, Ui.Pad(0)), _loading, _printerScroll);

            Control pin;
            if (hasPin)
            {
                pin = Make.Wrap(Strings.SharingDialogPinSet, ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0));
            }
            else
            {
                Label pinLabel = Make.Label(Strings.SharingDialogPin, ThemeFonts.SecondarySemi, Palette.Text, Ui.Pad(0));
                Label recommended = Make.Label(Strings.SharingDialogRecommended, ThemeFonts.Secondary, Palette.AccentText, Ui.Pad(4, 0, 0, 0));
                _pin = PinBox();
                _pin.Box.AccessibleName = Strings.SharingDialogPin;
                _again = PinBox();
                _again.Box.AccessibleName = Strings.SharingDialogPinAgain;
                Label again = Make.Label(Strings.SharingDialogPinAgain, ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0));
                pin = Make.Stack(6, Make.Row(pinLabel, recommended), _pin, again, _again,
                    Make.Wrap(Strings.SharingDialogPinHelp, ThemeFonts.Secondary, Palette.Muted, Ui.Pad(0)));
            }
            _problem = Make.Wrap("", ThemeFonts.SecondarySemi, Palette.DangerText, Ui.Pad(0));
            _problem.Visible = false;

            _content = Make.Stack(20, heading, printers, pin, _problem);
            _content.Dock = DockStyle.Top;
            var body = new Panel { Dock = DockStyle.Fill, Padding = Ui.Pad(28, 26, 28, 10), BackColor = Palette.Card };
            body.Controls.Add(_content);

            _ok = new ThemedButton(Strings.ShareTurnOn, ButtonKind.Primary) { Margin = Ui.Pad(0), Enabled = false };
            _ok.Click += (s, e) => TryAccept();
            var cancel = new ThemedButton(Strings.CopiesCancel, ButtonKind.Secondary) { DialogResult = DialogResult.Cancel, Margin = Ui.Pad(0, 0, 10, 0) };
            var footer = new Footer { Dock = DockStyle.Bottom, Height = Ui.S(76), Padding = Ui.Pad(28, 16, 28, 16) };
            FlowLayoutPanel buttons = Make.Row(cancel, _ok);
            buttons.WrapContents = false;
            buttons.Dock = DockStyle.Right;
            footer.Controls.Add(buttons);

            Controls.Add(body);
            Controls.Add(footer);
            AcceptButton = _ok;
            CancelButton = cancel;
            Ui.Solidify(this);
            footer.BackColor = Palette.DialogFooter;
            buttons.BackColor = Palette.DialogFooter;
        }

        /// <summary>Printer name → share it; set after OK.</summary>
        public Dictionary<string, bool> Choices { get; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>The new office PIN, or null to keep the PIN as it is (none, or the one already set).</summary>
        public string Pin { get; private set; }

        private static InputBox PinBox()
        {
            var box = new InputBox(240, 40) { Margin = Ui.Pad(0) };
            box.Box.UseSystemPasswordChar = true;
            box.Box.MaxLength = PinDialog.MaxLength;
            return box;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeTheme.ApplyTitleBar(this);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LoadPrinters();
        }

        private void LoadPrinters()
        {
            _host.ListPrintersAsync().ContinueWith(task =>
            {
                if (IsDisposed) return;
                if (task.IsFaulted)
                {
                    Exception ex = task.Exception == null ? new Exception("?") : task.Exception.GetBaseException();
                    Log.Error("Turn on sharing: the printer list could not be read.", ex);
                    _loading.Text = string.Format(Strings.SharePrintersError, ex.Message);
                    return;
                }
                IList<LocalPrinterInfo> printers = task.Result;
                if (printers.Count == 0)
                {
                    _loading.Text = Strings.ShareNoPrinters;
                    return;
                }
                _loading.Visible = false;
                _printers.SuspendLayout();
                foreach (LocalPrinterInfo printer in printers)
                {
                    var tick = new TickBox(printer.Name) { Checked = _host.FindShared(printer.Name) != null, Dock = DockStyle.Fill };
                    tick.CheckedChanged += (s, e) => UpdateOk();
                    _ticks.Add(tick);
                    var row = new Card { Radius = 6, Padding = Ui.Pad(12, 0, 12, 0), Edge = Palette.NestedBorder, Height = Ui.S(44), Dock = DockStyle.Fill, Margin = Ui.Pad(0, 0, 0, 6) };
                    row.Controls.Add(tick);
                    row.Click += (s, e) => tick.Checked = !tick.Checked;
                    _printers.RowCount++;
                    _printers.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.S(50)));
                    _printers.Controls.Add(row, 0, _printers.RowCount - 1);
                }
                Ui.Solidify(_printers);
                _printers.ResumeLayout(true);
                _printerScroll.Height = Math.Min(Ui.S(MaxListHeightPx), printers.Count * Ui.S(50));
                UpdateOk();
                FitHeight();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>The window as tall as its content, within the screen.</summary>
        private void FitHeight()
        {
            int content = _content.GetPreferredSize(new Size(ClientSize.Width - Ui.S(56), 0)).Height;
            int wanted = content + Ui.S(26 + 10 + 76 + 8);
            int max = Screen.FromControl(this).WorkingArea.Height - Ui.S(80);
            ClientSize = new Size(ClientSize.Width, Math.Min(max, Math.Max(Ui.S(360), wanted)));
        }

        private void UpdateOk()
        {
            _ok.Enabled = _ticks.Any(t => t.Checked);
        }

        private void TryAccept()
        {
            if (!_ticks.Any(t => t.Checked))
            {
                ShowProblem(Strings.SharingDialogNoPrinter);
                return;
            }
            string pin = null;
            if (!_hasPin && _pin.Box.Text.Length > 0)
            {
                string problem = PinDialog.Check(_pin.Box.Text, _again.Box.Text);
                if (problem != null)
                {
                    ShowProblem(problem);
                    return;
                }
                pin = _pin.Box.Text;
            }
            Choices.Clear();
            foreach (TickBox tick in _ticks)
            {
                Choices[tick.Text] = tick.Checked;
            }
            Pin = pin;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ShowProblem(string text)
        {
            _problem.Text = text;
            _problem.Visible = true;
            FitHeight();
        }

        /// <summary>The strip with the buttons: the footer colour and a line along its top.</summary>
        private sealed class Footer : Panel
        {
            public Footer()
            {
                SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var pen = new Pen(Palette.Divider))
                {
                    e.Graphics.DrawLine(pen, 0, 0, Width, 0);
                }
            }
        }
    }
}
