using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// The design's tick box: 20 px with 5 px corners, green with a white tick when ticked, outlined when not (0.4.1
    /// made the list's boxes this size at the owner's request). A CheckBox underneath: Space ticks it, screen readers
    /// read it, and a click anywhere on it (the box or its text) toggles it.
    /// </summary>
    internal sealed class TickBox : CheckBox
    {
        private const int Box = 20;
        private const int Gap = 12;

        public TickBox(string text = "")
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            Text = text;
            Font = ThemeFonts.TextMedium;
            ForeColor = Palette.Text;
            AutoSize = true;
            Cursor = Cursors.Hand;
            Margin = Ui.Pad(0);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            if (string.IsNullOrEmpty(Text))
            {
                return new Size(Ui.S(Box + 4), Ui.S(Box + 4));
            }
            Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            return new Size(Ui.S(Box + Gap) + text.Width + Ui.S(4), Math.Max(Ui.S(Box + 4), text.Height + Ui.S(4)));
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            Invalidate();
            base.OnCheckedChanged(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.Clear(Ui.BackOf(Parent));
            int size = Ui.S(Box);
            var box = new RectangleF(Ui.S(2), (Height - size) / 2f, size, size);
            Draw(g, box, Checked, Enabled);
            if (Focused && ShowFocusCues)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath ring = Ui.RoundRect(RectangleF.Inflate(box, Ui.S(2), Ui.S(2)), Ui.S(6)))
                using (var pen = new Pen(Palette.Accent, Ui.S(2)))
                {
                    g.DrawPath(pen, ring);
                }
            }
            if (!string.IsNullOrEmpty(Text))
            {
                var textRect = new Rectangle(Ui.S(Box + Gap), 0, Width - Ui.S(Box + Gap), Height);
                TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? ForeColor : Palette.Disabled,
                    TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            }
        }

        /// <summary>The box itself, also drawn inside list cells.</summary>
        public static void Draw(Graphics g, RectangleF box, bool on, bool enabled = true)
        {
            SmoothingMode before = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.RoundRect(box, Ui.S(5)))
            {
                if (on)
                {
                    using (var fill = new SolidBrush(enabled ? Palette.Accent : Palette.Disabled)) g.FillPath(fill, path);
                    using (var pen = new Pen(Palette.OnAccent, Ui.S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    {
                        g.DrawLines(pen, new[]
                        {
                            new PointF(box.X + box.Width * 0.26f, box.Y + box.Height * 0.52f),
                            new PointF(box.X + box.Width * 0.43f, box.Y + box.Height * 0.70f),
                            new PointF(box.X + box.Width * 0.75f, box.Y + box.Height * 0.32f)
                        });
                    }
                }
                else
                {
                    using (var fill = new SolidBrush(enabled ? Palette.TickOffFill : Palette.DisabledBg)) g.FillPath(fill, path);
                    using (var pen = new Pen(enabled ? Palette.TickOffBorder : Palette.DisabledBorder, Ui.S(1) * 1.5f)) g.DrawPath(pen, path);
                }
            }
            g.SmoothingMode = before;
        }
    }
}
