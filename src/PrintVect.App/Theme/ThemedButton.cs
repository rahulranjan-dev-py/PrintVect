using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    internal enum ButtonKind
    {
        /// <summary>Accent green with dark text: the one main action of a card.</summary>
        Primary,
        /// <summary>Outlined (#3A4350) on the card: every other action.</summary>
        Secondary,
        /// <summary>Dark on the yellow update strip.</summary>
        OnWarning
    }

    /// <summary>
    /// A button of the design (8 px corners, at least 44 px tall). Still a real Button underneath, so the keyboard,
    /// Enter / Space, the focus order and screen readers work as before.
    /// </summary>
    internal sealed class ThemedButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public ThemedButton(string text, ButtonKind kind)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            Kind = kind;
            Text = text;
            Font = ThemeFonts.TextSemi;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Ui.Pad(0, 0, 8, 0);
            Cursor = Cursors.Hand;
            MinimumHeight = kind == ButtonKind.OnWarning ? 36 : 44;
        }

        public ButtonKind Kind { get; set; }

        /// <summary>Design pixels at 100 %.</summary>
        public int MinimumHeight { get; set; }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text ?? "", Font);
            return new Size(text.Width + Ui.S(32), Math.Max(Ui.S(MinimumHeight), text.Height + Ui.S(16)));
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            _pressed = mevent.Button == MouseButtons.Left;
            Invalidate();
            base.OnMouseDown(mevent);
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            _pressed = false;
            Invalidate();
            base.OnMouseUp(mevent);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.Clear(Ui.BackOf(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill, text, border;
            Colours(out fill, out text, out border);

            var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Ui.RoundRect(bounds, Ui.S(8)))
            {
                if (fill != Color.Transparent)
                {
                    using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                }
                if (border != Color.Transparent)
                {
                    using (var pen = new Pen(border, 1f)) g.DrawPath(pen, path);
                }
            }
            if (Focused && ShowFocusCues)
            {
                var ring = new RectangleF(2f, 2f, Width - 5f, Height - 5f);
                using (GraphicsPath path = Ui.RoundRect(ring, Ui.S(6)))
                using (var pen = new Pen(Kind == ButtonKind.Primary ? Palette.OnAccent : Palette.Accent, 1.5f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawPath(pen, path);
                }
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void Colours(out Color fill, out Color text, out Color border)
        {
            switch (Kind)
            {
                case ButtonKind.Primary:
                    border = Color.Transparent;
                    if (!Enabled)
                    {
                        fill = Palette.Border;
                        text = Palette.Muted;
                    }
                    else
                    {
                        fill = _pressed ? Shade(Palette.Accent, -0.15f) : _hover ? Shade(Palette.Accent, 0.12f) : Palette.Accent;
                        text = Palette.OnAccent;
                    }
                    return;
                case ButtonKind.OnWarning:
                    border = Color.Transparent;
                    fill = !Enabled ? Shade(Palette.Warning, -0.2f)
                        : _pressed ? Shade(Palette.OnWarning, 0.25f) : _hover ? Shade(Palette.OnWarning, 0.15f) : Palette.OnWarning;
                    text = Enabled ? Palette.Warning : Palette.OnWarning;
                    return;
                default:
                    fill = _pressed ? Palette.Selection : _hover ? Palette.Hover : Color.Transparent;
                    border = Enabled ? Palette.Outline : Palette.Border;
                    text = Enabled ? Palette.Text : Palette.Muted;
                    return;
            }
        }

        /// <summary>Lighter (amount &gt; 0, towards white) or darker (amount &lt; 0, towards black).</summary>
        internal static Color Shade(Color c, float amount)
        {
            Func<int, int> f = v => amount >= 0 ? (int)(v + (255 - v) * amount) : (int)(v * (1 + amount));
            return Color.FromArgb(c.A, f(c.R), f(c.G), f(c.B));
        }
    }
}
