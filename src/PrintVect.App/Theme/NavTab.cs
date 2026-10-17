using System;
using System.Drawing;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// One tab of the top bar: muted text, white when active with a 3 px green line under it. A Button underneath,
    /// so Tab moves between them and Enter / Space opens one.
    /// </summary>
    internal sealed class NavTab : Button
    {
        private bool _active;
        private bool _hover;

        public NavTab(string text)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            Text = text;
            Font = ThemeFonts.TextSemi;
            AutoSize = true;
            Cursor = Cursors.Hand;
            Margin = Ui.Pad(0);
            Dock = DockStyle.Left;
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

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = TextRenderer.MeasureText(Text ?? "", Font);
            return new Size(text.Width + Ui.S(32), Ui.S(56));
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
            g.Clear(Palette.TopBar);
            Color text = _active || _hover ? Palette.Text : Palette.Muted;
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            if (_active)
            {
                int line = Ui.S(3);
                using (var brush = new SolidBrush(Palette.Accent))
                {
                    g.FillRectangle(brush, Ui.S(12), Height - line, Width - Ui.S(24), line);
                }
            }
            if (Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(g, new Rectangle(Ui.S(6), Ui.S(12), Width - Ui.S(12), Height - Ui.S(24)),
                    Palette.Text, Palette.TopBar);
            }
        }
    }
}
