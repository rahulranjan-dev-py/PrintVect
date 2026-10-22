using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// A card of the design: the card colour with a 1 px border and 10 px corners, 20 px inside. Its height follows its
    /// one content control (a column of rows), so cards stack on a page without fixed sizes.
    /// </summary>
    internal class Card : Panel
    {
        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            BackColor = Palette.Card;
            ForeColor = Palette.Text;
            Padding = Ui.Pad(20, 18, 20, 18);
            Margin = Ui.Pad(0);
            Radius = 10;
        }

        /// <summary>A box inside a card (the look-up result): the header colour with a lighter border, 8 px corners.</summary>
        public static Card Nested()
        {
            var card = new Card { Radius = 8, Padding = Ui.Pad(0) };
            card.Fill = Palette.HeaderBg;
            card.Edge = Palette.NestedBorder;
            card.BackColor = Palette.HeaderBg;
            return card;
        }

        /// <summary>Corner radius in design pixels.</summary>
        public int Radius { get; set; }

        /// <summary>Overrides the card colour (null: the card colour of the theme).</summary>
        public Color? Fill { get; set; }

        /// <summary>Overrides the border colour (null: the border colour of the theme; transparent: none).</summary>
        public Color? Edge { get; set; }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Control content = Controls.Count == 1 ? Controls[0] : null;
            if (content == null)
            {
                return base.GetPreferredSize(proposedSize);
            }
            int width = proposedSize.Width > 0 && proposedSize.Width < 100000 ? proposedSize.Width : Math.Max(Width, 1);
            Size inner = content.GetPreferredSize(new Size(Math.Max(1, width - Padding.Horizontal), 0));
            return new Size(width, inner.Height + Padding.Vertical);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color outside = Ui.BackOf(Parent);
            e.Graphics.Clear(outside);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color edge = Edge ?? Palette.Border;
            using (GraphicsPath path = Ui.RoundRect(bounds, Ui.S(Radius)))
            using (var fill = new SolidBrush(Fill ?? Palette.Card))
            {
                e.Graphics.FillPath(fill, path);
                if (edge.A > 0)
                {
                    using (var border = new Pen(edge, 1f)) e.Graphics.DrawPath(border, path);
                }
            }
        }
    }
}
