using System;
using System.Drawing;
using System.Windows.Forms;
using PrintVect.App.Theme;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// The PIN screen (M6b-2): sets, changes or removes the office PIN. Every PC in the office must use the same PIN;
    /// a host with a PIN refuses jobs that come with a different one, and those jobs wait for Retry on the sender.
    /// </summary>
    internal sealed class PinDialog : Form
    {
        public const int MinLength = 4;
        public const int MaxLength = 12;

        private readonly InputBox _pin;
        private readonly InputBox _again;
        private readonly Label _problem;

        public PinDialog(bool hasPin)
        {
            AutoScaleMode = AutoScaleMode.None;
            Text = Strings.PinTitle;
            Icon = AppIcon.Window;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Palette.Background;
            ForeColor = Palette.Text;
            Font = ThemeFonts.Text;
            ClientSize = new Size(Ui.S(460), Ui.S(400));

            var outer = new Panel { Dock = DockStyle.Fill, Padding = Ui.Pad(16), BackColor = Palette.Background };
            var card = new Card { Dock = DockStyle.Fill, Margin = Ui.Pad(0) };
            outer.Controls.Add(card);

            TableLayoutPanel column = Make.Column(0, 0, 0, 0, 0, 0, 100, 0);
            column.Controls.Add(Make.Title(hasPin ? Strings.PinChangeTitle : Strings.PinSetTitle), 0, 0);
            column.Controls.Add(Make.Muted(Strings.PinExplain), 0, 1);
            column.Controls.Add(Make.Caption(Strings.PinNew), 0, 2);
            _pin = PasswordBox();
            column.Controls.Add(_pin, 0, 3);
            column.Controls.Add(Make.Caption(Strings.PinAgain), 0, 4);
            _again = PasswordBox();
            column.Controls.Add(_again, 0, 5);
            _problem = Make.Wrap("", ThemeFonts.Secondary, Palette.Warning, Ui.Pad(0, 8, 0, 0));
            column.Controls.Add(_problem, 0, 6);

            var save = new ThemedButton(Strings.PinSave, ButtonKind.Primary);
            save.Click += (s, e) => TrySave();
            var cancel = new ThemedButton(Strings.CopiesCancel, ButtonKind.Secondary) { DialogResult = DialogResult.Cancel };
            column.Controls.Add(Make.Row(save, cancel), 0, 7);
            card.Controls.Add(column);
            Controls.Add(outer);

            AcceptButton = save;
            CancelButton = cancel;
            Ui.Solidify(this);
        }

        /// <summary>The PIN to save; valid after DialogResult.OK.</summary>
        public string Pin { get; private set; }

        private static InputBox PasswordBox()
        {
            var box = new InputBox(220) { Margin = Ui.Pad(0, 0, 0, 12) };
            box.Box.UseSystemPasswordChar = true;
            box.Box.MaxLength = MaxLength;
            return box;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _pin.Box.Focus();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeTheme.UseDarkTitleBar(this);
        }

        private void TrySave()
        {
            string problem = Check(_pin.Box.Text, _again.Box.Text);
            if (problem != null)
            {
                _problem.Text = problem;
                return;
            }
            Pin = _pin.Box.Text;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Why the two entries cannot be saved, or null. The rule: 4 to 12 characters, no spaces at the ends, typed twice alike.</summary>
        internal static string Check(string pin, string again)
        {
            pin = pin ?? "";
            if (pin.Length < MinLength || pin.Length > MaxLength)
            {
                return string.Format(Strings.PinLength, MinLength, MaxLength);
            }
            if (pin.Trim() != pin)
            {
                return Strings.PinSpaces;
            }
            if (pin != (again ?? ""))
            {
                return Strings.PinMismatch;
            }
            return null;
        }
    }
}
