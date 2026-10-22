using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// One entry of the side menu: icon and name, 44 px tall; the open page has the card colour, a soft shadow and a
    /// 3 px green bar on the left. A Button underneath, so Tab moves between them and Enter / Space opens one.
    /// </summary>
    internal sealed class NavItem : Button
    {
        /// <summary>44 px of item and 4 px of gap below it (docked items ignore margins).</summary>
        public const int HeightPx = 48;

        private readonly Glyph _glyph;
        private bool _active;
        private bool _hover;

        public NavItem(string text, Glyph glyph)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            _glyph = glyph;
            Text = text;
            Font = ThemeFonts.TextMedium;
            Cursor = Cursors.Hand;
            Margin = Ui.Pad(0);
            Height = Ui.S(HeightPx);
            Dock = DockStyle.Top;
        }

        public bool Active
        {
            get { return _active; }
            set
            {
                _active = value;
                AccessibleDescription = value ? "selected" : null;
                Invalidate();
            }
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
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.Clear(Ui.BackOf(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int h = Height - Ui.S(4);
            if (_active)
            {
                using (GraphicsPath shadow = Ui.RoundRect(new RectangleF(0, 1, Width - 1, h), Ui.S(6)))
                using (var brush = new SolidBrush(Palette.Shadow))
                {
                    g.FillPath(brush, shadow);
                }
                using (GraphicsPath path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, h - 1), Ui.S(6)))
                using (var brush = new SolidBrush(Palette.NavActive))
                {
                    g.FillPath(brush, path);
                }
                using (GraphicsPath bar = Ui.RoundRect(new RectangleF(Ui.S(8), (h - Ui.S(18)) / 2f, Ui.S(3), Ui.S(18)), Ui.S(2)))
                using (var brush = new SolidBrush(Palette.Accent))
                {
                    g.FillPath(brush, bar);
                }
            }
            else if (_hover)
            {
                using (GraphicsPath path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, h - 1), Ui.S(6)))
                using (var brush = new SolidBrush(Palette.NavHover))
                {
                    g.FillPath(brush, path);
                }
            }
            Color ink = _active ? Palette.Text : Palette.TextSecondary;
            int iconSize = Ui.S(20);
            int x = Ui.S(8 + 3 + 12);
            Icons.Draw(g, _glyph, new RectangleF(x, (h - iconSize) / 2f, iconSize, iconSize), ink);
            x += iconSize + Ui.S(12);
            TextRenderer.DrawText(g, Text, _active ? ThemeFonts.TextSemi : ThemeFonts.TextMedium, new Rectangle(x, 0, Width - x - Ui.S(8), h), ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
            {
                using (GraphicsPath ring = Ui.RoundRect(new RectangleF(1.5f, 1.5f, Width - 4f, h - 4f), Ui.S(5)))
                using (var pen = new Pen(Palette.Accent, Ui.S(2)))
                {
                    g.DrawPath(pen, ring);
                }
            }
        }
    }
}
