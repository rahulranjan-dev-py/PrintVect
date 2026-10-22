using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using PrintVect.App.Theme;
using PrintVect.Core.Client;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// The copies prompt (M6b-2): appears when a program prints to a PrintVect printer, before the job leaves this PC.
    /// The XPS writer usually loses the copies chosen in the program's print window (brief 11.6), so the clerk says
    /// it here. − and + (or the arrow keys, or typing a number), Enter prints, Esc cancels. Nobody at the PC: after
    /// a minute it prints with the number shown, so a job is never stuck behind an unanswered window.
    /// </summary>
    internal sealed class CopiesDialog : Form
    {
        public const int AutoPrintSeconds = 60;
        private const int MaxCopies = 99;

        private readonly Label _number;
        private readonly ThemedButton _print;
        private readonly ToggleSwitch _dontAsk;
        private readonly Timer _countdown;
        private int _copies;
        private int _secondsLeft = AutoPrintSeconds;
        private bool _answered;

        public CopiesDialog(ClientJobRecord job)
        {
            _copies = Math.Max(1, Math.Min(MaxCopies, job.Copies));

            AutoScaleMode = AutoScaleMode.None;
            Text = Strings.CopiesTitle;
            Icon = AppIcon.Window;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            ShowInTaskbar = true;
            KeyPreview = true;
            BackColor = Palette.Background;
            ForeColor = Palette.Text;
            Font = ThemeFonts.Text;
            ClientSize = new Size(Ui.S(440), Ui.S(330));

            var card = new Card { Dock = DockStyle.Fill, Margin = Ui.Pad(0) };
            var outer = new Panel { Dock = DockStyle.Fill, Padding = Ui.Pad(16), BackColor = Palette.Background };
            outer.Controls.Add(card);

            TableLayoutPanel column = Make.Column(0, 0, 0, 100, 0, 0);
            column.Controls.Add(Make.Label(string.Format(Strings.CopiesDocument, job.Doc), ThemeFonts.Heading, Palette.Text, Ui.Pad(0, 0, 0, 4)), 0, 0);
            Label where = Make.Muted(string.Format(Strings.CopiesWhere, job.PrinterFriendly, job.HostName));
            where.Margin = Ui.Pad(0, 0, 0, 12);
            column.Controls.Add(where, 0, 1);
            column.Controls.Add(Make.Caption(Strings.CopiesLabel), 0, 2);

            var minus = new ThemedButton(Strings.CopiesFewer, ButtonKind.Secondary, Glyph.Minus) { Margin = Ui.Pad(0), IconOnly = true };
            minus.Click += (s, e) => Change(-1);
            var plus = new ThemedButton(Strings.CopiesMore, ButtonKind.Secondary, Glyph.Plus) { Margin = Ui.Pad(0), IconOnly = true };
            plus.Click += (s, e) => Change(+1);
            _number = new Label
            {
                Font = ThemeFonts.Number,
                ForeColor = Palette.Text,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(Ui.S(90), Ui.S(44)),
                Margin = Ui.Pad(8, 0, 8, 0)
            };
            FlowLayoutPanel stepper = Make.Row(minus, _number, plus);
            stepper.Margin = Ui.Pad(0, 0, 0, 8);
            column.Controls.Add(stepper, 0, 3);

            _dontAsk = new ToggleSwitch(Strings.CopiesDontAsk)
            {
                Font = ThemeFonts.Secondary,
                ForeColor = Palette.Muted,
                WrapWidth = 320,
                Margin = Ui.Pad(0, 0, 0, 10)
            };
            column.Controls.Add(_dontAsk, 0, 4);

            _print = new ThemedButton("", ButtonKind.Primary);
            _print.Click += (s, e) => Answer(_copies);
            var cancel = new ThemedButton(Strings.CopiesCancel, ButtonKind.Secondary) { Margin = Ui.Pad(0) };
            cancel.Click += (s, e) => Answer(null);
            column.Controls.Add(Make.Row(_print, cancel), 0, 5);
            card.Controls.Add(column);
            Controls.Add(outer);

            AcceptButton = _print;
            CancelButton = cancel;
            ShowNumber();
            Ui.Solidify(this);

            _countdown = new Timer { Interval = 1000 };
            _countdown.Tick += (s, e) =>
            {
                _secondsLeft--;
                if (_secondsLeft <= 0)
                {
                    Answer(_copies);
                    return;
                }
                ShowNumber();
            };
        }

        /// <summary>Raised once with the copies to print, or null for Cancel, and whether to stop asking on this PC.</summary>
        public event Action<int?, bool> Answered;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _countdown.Start();
            Activate();
            _print.Focus();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeTheme.ApplyTitleBar(this);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Up:
                case Keys.Add:
                case Keys.Oemplus:
                    Change(+1);
                    return true;
                case Keys.Down:
                case Keys.Subtract:
                case Keys.OemMinus:
                    Change(-1);
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>Typing digits sets the number directly: "1", "2" gives 12 within two seconds.</summary>
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (char.IsDigit(e.KeyChar))
            {
                int digit = e.KeyChar - '0';
                bool continuing = DateTime.UtcNow - _lastDigit < TimeSpan.FromSeconds(2) && _copies < 10;
                int value = continuing ? _copies * 10 + digit : digit;
                _lastDigit = DateTime.UtcNow;
                if (value >= 1)
                {
                    Set(value);
                }
                e.Handled = true;
            }
        }

        private DateTime _lastDigit = DateTime.MinValue;

        private void Change(int delta)
        {
            Set(_copies + delta);
        }

        private void Set(int value)
        {
            _copies = Math.Max(1, Math.Min(MaxCopies, value));
            _secondsLeft = AutoPrintSeconds;   // someone is at the PC: give them the full minute again
            ShowNumber();
        }

        private void ShowNumber()
        {
            _number.Text = _copies.ToString(CultureInfo.CurrentCulture);
            _print.Text = string.Format(Strings.CopiesPrint, _secondsLeft);
        }

        private void Answer(int? copies)
        {
            if (_answered)
            {
                return;
            }
            _answered = true;
            _countdown.Stop();
            Answered?.Invoke(copies, _dontAsk.Checked);
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (!_answered && e.CloseReason == CloseReason.UserClosing)
            {
                Answer(null);   // the window's X is Cancel
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _countdown.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
