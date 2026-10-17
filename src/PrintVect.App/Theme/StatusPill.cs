using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    internal enum PillKind
    {
        Connected,
        Offline,
        Warning
    }

    /// <summary>A fully rounded status pill: "Sharing ON" in green, "Sharing OFF" in grey.</summary>
    internal sealed class StatusPill : Control
    {
        private PillKind _kind = PillKind.Offline;

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

        public PillKind Kind
        {
            get { return _kind; }
            set
            {
                _kind = value;
                Invalidate();
            }
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
            Size text = TextRenderer.MeasureText(Text ?? "", Font);
            return new Size(text.Width + Ui.S(26), Math.Max(Ui.S(26), text.Height + Ui.S(8)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Ui.BackOf(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color back, fore;
            switch (_kind)
            {
                case PillKind.Connected:
                    back = Palette.ConnectedPill;
                    fore = Palette.ConnectedText;
                    break;
                case PillKind.Warning:
                    back = Palette.Warning;
                    fore = Palette.OnWarning;
                    break;
                default:
                    back = Palette.OfflinePill;
                    fore = Palette.OfflineText;
                    break;
            }
            using (GraphicsPath path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f))
            using (var brush = new SolidBrush(back))
            {
                g.FillPath(brush, path);
            }
            // A dot before the text, as in the mock-up.
            int dot = Ui.S(6);
            using (var brush = new SolidBrush(fore))
            {
                g.FillEllipse(brush, Ui.S(10), (Height - dot) / 2f, dot, dot);
            }
            var textRect = new Rectangle(Ui.S(20), 0, Width - Ui.S(24), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }
}
