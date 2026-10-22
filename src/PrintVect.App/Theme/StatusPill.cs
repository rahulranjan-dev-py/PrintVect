using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    internal enum PillKind
    {
        /// <summary>Green: Ready, Sharing on, Connected, Printed.</summary>
        Success,
        /// <summary>Grey: Off, Last seen, Cancelled, not found.</summary>
        Neutral,
        /// <summary>Yellow: Paused, Waiting, Not set.</summary>
        Warning,
        /// <summary>Red: Failed, printer error.</summary>
        Danger,
        /// <summary>Blue: Sending, Printing.</summary>
        Info,
        /// <summary>A grey number with no dot: the count beside a section title.</summary>
        Count
    }

    /// <summary>A fully rounded status pill with a dot before the text (24 px tall).</summary>
    internal sealed class StatusPill : Control
    {
        private PillKind _kind = PillKind.Neutral;

        public StatusPill()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Font = ThemeFonts.Small;
            AutoSize = true;
            Margin = Ui.Pad(0);
        }

        public StatusPill(string text, PillKind kind) : this()
        {
            _kind = kind;
            Text = text;
        }

        public PillKind Kind
        {
            get { return _kind; }
            set
            {
                _kind = value;
                if (AutoSize) Size = GetPreferredSize(Size.Empty);
                Invalidate();
            }
        }

        public void Set(string text, PillKind kind)
        {
            _kind = kind;
            if (Text == text)
            {
                if (AutoSize) Size = GetPreferredSize(Size.Empty);
                Invalidate();
                return;
            }
            Text = text;
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (AutoSize)
            {
                Size = GetPreferredSize(Size.Empty);
            }
            Invalidate();
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            return Measure(Text, _kind);
        }

        public static Size Measure(string text, PillKind kind)
        {
            Size size = TextRenderer.MeasureText(text ?? "", ThemeFonts.Small, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            int lead = kind == PillKind.Count ? Ui.S(8) : Ui.S(23);
            return new Size(Math.Max(Ui.S(24), size.Width + lead + Ui.S(9)), Math.Max(Ui.S(24), size.Height + Ui.S(6)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Ui.BackOf(Parent));
            Draw(e.Graphics, new Rectangle(0, 0, Width, Height), Text, _kind);
        }

        /// <summary>Draws a pill into <paramref name="bounds"/> (also used inside list cells).</summary>
        public static void Draw(Graphics g, Rectangle bounds, string text, PillKind kind)
        {
            Color back, fore, dot;
            Colours(kind, out back, out fore, out dot);
            SmoothingMode before = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.RoundRect(new RectangleF(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1), bounds.Height / 2f))
            using (var brush = new SolidBrush(back))
            {
                g.FillPath(brush, path);
            }
            int textLeft = bounds.X + Ui.S(8);
            if (kind != PillKind.Count)
            {
                int size = Ui.S(7);
                using (var brush = new SolidBrush(dot))
                {
                    g.FillEllipse(brush, bounds.X + Ui.S(10), bounds.Y + (bounds.Height - size) / 2f, size, size);
                }
                textLeft = bounds.X + Ui.S(23);
            }
            g.SmoothingMode = before;
            var textRect = new Rectangle(textLeft, bounds.Y, Math.Max(1, bounds.Right - textLeft - Ui.S(6)), bounds.Height);
            TextRenderer.DrawText(g, text ?? "", ThemeFonts.Small, textRect, fore,
                (kind == PillKind.Count ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left) | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        private static void Colours(PillKind kind, out Color back, out Color fore, out Color dot)
        {
            switch (kind)
            {
                case PillKind.Success:
                    back = Palette.SuccessBg; fore = Palette.SuccessText; dot = Palette.SuccessDot;
                    return;
                case PillKind.Warning:
                    back = Palette.WarningBg; fore = Palette.WarningText; dot = Palette.WarningDot;
                    return;
                case PillKind.Danger:
                    back = Palette.DangerBg; fore = Palette.DangerText; dot = Palette.DangerDot;
                    return;
                case PillKind.Info:
                    back = Palette.InfoBg; fore = Palette.InfoText; dot = Palette.InfoIcon;
                    return;
                case PillKind.Count:
                    back = Palette.Badge; fore = Palette.BadgeText; dot = Palette.BadgeText;
                    return;
                default:
                    back = Palette.NeutralBg; fore = Palette.NeutralText; dot = Palette.NeutralDot;
                    return;
            }
        }
    }
}
