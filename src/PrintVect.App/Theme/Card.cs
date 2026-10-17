using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>A card of the design: #181C22 with a 1 px #262D37 border and 12 px corners, 20 px inside.</summary>
    internal class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            BackColor = Palette.Card;
            ForeColor = Palette.Text;
            Padding = Ui.Pad(20, 18, 20, 18);
            Margin = Ui.Pad(0, 0, 16, 16);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color outside = Ui.BackOf(Parent);
            e.Graphics.Clear(outside);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            using (GraphicsPath path = Ui.RoundRect(bounds, Ui.S(12)))
            using (var fill = new SolidBrush(Palette.Card))
            using (var border = new Pen(Palette.Border, 1f))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
        }
    }
}
