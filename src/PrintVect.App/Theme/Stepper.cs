using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>− value + in one outlined box (Settings, Keep copies of sent documents). The two halves are real buttons.</summary>
    internal sealed class Stepper : Panel
    {
        private readonly Label _value;
        private readonly ThemedButton _less;
        private readonly ThemedButton _more;

        public Stepper(string lessName, string moreName)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            BackColor = Palette.Card;
            Size = new Size(Ui.S(40 + 96 + 40), Ui.S(40));
            Margin = Ui.Pad(0);
            _less = new ThemedButton("", ButtonKind.Ghost, Glyph.Minus) { IconOnly = true, MinimumHeight = 38, AccessibleName = lessName, Margin = Ui.Pad(0) };
            _more = new ThemedButton("", ButtonKind.Ghost, Glyph.Plus) { IconOnly = true, MinimumHeight = 38, AccessibleName = moreName, Margin = Ui.Pad(0) };
            _less.AutoSize = false;
            _more.AutoSize = false;
            _less.Bounds = new Rectangle(1, 1, Ui.S(38), Ui.S(38));
            _more.Bounds = new Rectangle(Width - Ui.S(39), 1, Ui.S(38), Ui.S(38));
            _value = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = ThemeFonts.TextSemi,
                ForeColor = Palette.Text,
                BackColor = Palette.Card,
                Bounds = new Rectangle(Ui.S(40), 1, Width - Ui.S(80), Ui.S(38)),
                UseMnemonic = false
            };
            _less.Click += (s, e) => Less?.Invoke(this, EventArgs.Empty);
            _more.Click += (s, e) => More?.Invoke(this, EventArgs.Empty);
            Controls.Add(_less);
            Controls.Add(_value);
            Controls.Add(_more);
        }

        public event EventHandler Less;
        public event EventHandler More;

        public string Value
        {
            get { return _value.Text; }
            set
            {
                _value.Text = value ?? "";
                _value.AccessibleName = value;
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            return new Size(Ui.S(40 + 96 + 40), Ui.S(40));   // fixed: a layout must never shrink it
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Ui.BackOf(Parent));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Ui.S(6)))
            using (var fill = new SolidBrush(Palette.Card))
            using (var pen = new Pen(Palette.ButtonBorder))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
            using (var pen = new Pen(Palette.DisabledBorder))
            {
                e.Graphics.DrawLine(pen, Ui.S(39), 1, Ui.S(39), Height - 2);
                e.Graphics.DrawLine(pen, Width - Ui.S(40), 1, Width - Ui.S(40), Height - 2);
            }
        }
    }
}
