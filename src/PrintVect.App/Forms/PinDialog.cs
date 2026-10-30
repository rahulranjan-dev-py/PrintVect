using System;
using System.Drawing;
using System.Windows.Forms;
using PrintVect.App.Theme;

namespace PrintVect.App.Forms
{
    /// <summary>
    /// The PIN screen (M6b-2; in the colours of the theme since 0.5.0): sets, changes or removes the office PIN. Every PC in the office must use the same PIN;
    /// a host with a PIN refuses jobs that come with a different one, and those jobs wait for Retry on the sender.
    /// </summary>
    internal sealed class PinDialog : Form
    {
        /// <summary>
        /// 8 since 0.5.9 (was 4): someone who records the network can try every short PIN on their own computer. A
        /// shorter PIN saved before keeps working; Settings shows a note next to it (IsShort).
        /// </summary>
        public const int MinLength = 8;
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

            TableLayoutPanel column = Make.Column(0, 0, 0, 0, 0, 0, 0, 100, 0);
            column.Controls.Add(Make.Label(hasPin ? Strings.PinChangeTitle : Strings.PinSetTitle, ThemeFonts.Heading, Palette.Text, Ui.Pad(0, 0, 0, 8)), 0, 0);
            Label explain = Make.Muted(Strings.PinExplain);
            explain.Margin = Ui.Pad(0, 0, 0, 14);
            column.Controls.Add(explain, 0, 1);
            column.Controls.Add(Make.Caption(Strings.PinNew), 0, 2);
            _pin = PasswordBox();
            column.Controls.Add(_pin, 0, 3);
            column.Controls.Add(Make.Caption(Strings.PinAgain), 0, 4);
            _again = PasswordBox();
            column.Controls.Add(_again, 0, 5);
            _problem = Make.Wrap("", ThemeFonts.SecondarySemi, Palette.DangerText, Ui.Pad(0, 8, 0, 0));
            column.Controls.Add(_problem, 0, 6);

            var save = new ThemedButton(Strings.PinSave, ButtonKind.Primary);
            save.Click += (s, e) => TrySave();
            var cancel = new ThemedButton(Strings.CopiesCancel, ButtonKind.Secondary) { DialogResult = DialogResult.Cancel, Margin = Ui.Pad(0) };
            FlowLayoutPanel buttons = Make.Row(save, cancel);
            buttons.Margin = Ui.Pad(0, 12, 0, 0);
            column.Controls.Add(buttons, 0, 8);
            card.Controls.Add(column);
            Controls.Add(outer);

            AcceptButton = save;
            CancelButton = cancel;

            // As tall as the content with room for the longest message, which the empty row 7 gives back while there is
            // none (0.5.9: with a fixed height a four-line explanation left the message no room, so a refused PIN
            // showed nothing).
            _problem.Text = Strings.PinMismatch;
            int inner = ClientSize.Width - outer.Padding.Horizontal - card.Padding.Horizontal;
            ClientSize = new Size(ClientSize.Width, outer.Padding.Vertical + card.Padding.Vertical + column.GetPreferredSize(new Size(inner, 0)).Height);
            _problem.Text = "";
            Ui.Solidify(this);
        }

        /// <summary>The PIN to save; valid after DialogResult.OK.</summary>
        public string Pin { get; private set; }

        private static InputBox PasswordBox()
        {
            var box = new InputBox(240, 40) { Margin = Ui.Pad(0, 6, 0, 12) };
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
            NativeTheme.ApplyTitleBar(this);
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

        /// <summary>Why the two entries cannot be saved, or null. The rule: 8 to 12 characters (0.5.9; was 4), no spaces at the ends, typed twice alike.</summary>
        /// <summary>A saved PIN shorter than new ones may be (set before 0.5.9).</summary>
        internal static bool IsShort(string pin)
        {
            return !string.IsNullOrEmpty(pin) && pin.Length < MinLength;
        }

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
