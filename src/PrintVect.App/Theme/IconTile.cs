using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// A line icon on a rounded square or a circle: the logo, the printer on each printer card, the round icon of an
    /// empty list and the ticks of the Diagnostics checklist.
    /// </summary>
    internal sealed class IconTile : Control
    {
        private Glyph _glyph;
        private Color _fill;
        private Color _ink;
        private readonly int _glyphPx;
        private readonly bool _circle;
        private readonly int _radiusPx;
        private readonly float _stroke;
        private readonly Size _size;

        public IconTile(Glyph glyph, int sizePx, int glyphPx, Color fill, Color ink, bool circle = false, int radiusPx = 8, float stroke = 1.8f)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            _glyph = glyph;
            _fill = fill;
            _ink = ink;
            _glyphPx = glyphPx;
            _circle = circle;
            _radiusPx = radiusPx;
            _stroke = stroke;
            _size = new Size(Ui.S(sizePx), Ui.S(sizePx));
            Size = _size;
            Margin = Ui.Pad(0);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            return _size;
        }

        /// <summary>Changes the icon and its colours (a checklist line turning from a tick into a warning).</summary>
        public void Set(Glyph glyph, Color fill, Color ink)
        {
            _glyph = glyph;
            _fill = fill;
            _ink = ink;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Ui.BackOf(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(_fill))
            {
                if (_circle)
                {
                    g.FillEllipse(brush, 0, 0, Width - 1, Height - 1);
                }
                else
                {
                    using (GraphicsPath path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), Ui.S(_radiusPx)))
                    {
                        g.FillPath(brush, path);
                    }
                }
            }
            float size = Ui.S(_glyphPx);
            Icons.Draw(g, _glyph, new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size), _ink, _stroke);
        }
    }
}
