using System;
using System.Drawing;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// A page: scrolls when it is taller than the window, its column keeps 36 px from both edges and 28 px from the top,
    /// is never wider than 1400 px and is centred when the window is wider still.
    /// </summary>
    internal class PageScroller : Panel
    {
        /// <summary>
        /// 0.5.0 had the canvas's 1000 px, kept on the left: a maximised window on an office monitor then showed an
        /// empty band a third of its width on the right (owner's photos, 0.5.2).
        /// </summary>
        public const int MaxContentWidth = 1400;
        private readonly TableLayoutPanel _column;
        private int _rows;

        public PageScroller()
        {
            AutoScroll = true;
            BackColor = Palette.Background;
            Margin = Ui.Pad(0);
            _column = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Margin = Ui.Pad(0),
                Padding = Ui.Pad(0, 0, 0, 48),
                BackColor = Palette.Background
            };
            _column.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(_column);
            NativeTheme.UseThemedScrollBars(this);
            ApplyPadding();
        }

        /// <summary>Adds a block under the previous one, <paramref name="gapAbovePx"/> below it.</summary>
        public T Add<T>(T control, int gapAbovePx) where T : Control
        {
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(0, _rows == 0 ? 0 : Ui.S(gapAbovePx), 0, 0);
            _column.RowCount = _rows + 1;
            _column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _column.Controls.Add(control, 0, _rows);
            _rows++;
            return control;
        }

        protected override void OnResize(EventArgs eventargs)
        {
            ApplyPadding();
            base.OnResize(eventargs);
        }

        private void ApplyPadding()
        {
            Padding padding = SidePadding(ClientSize.Width, Ui.S(28), 0);
            if (Padding != padding) Padding = padding;
        }

        /// <summary>The edges of a page of this width: 36 px at least, the column at most MaxContentWidth and centred.</summary>
        public static Padding SidePadding(int width, int top, int bottom)
        {
            int side = Ui.S(36);
            int column = Math.Max(0, Math.Min(width - 2 * side, Ui.S(MaxContentWidth)));
            int left = Math.Max(side, (width - column) / 2);
            int right = Math.Max(side, width - left - column);
            return new Padding(left, top, right, bottom);
        }
    }

    /// <summary>
    /// Two columns that share the width like the canvas's flex boxes (a basis each and a share of what is left), and
    /// stack one above the other when the window is too narrow for both.
    /// </summary>
    internal sealed class TwoColumns : Panel
    {
        private readonly Control _left;
        private readonly Control _right;
        private readonly int _leftBasis, _rightBasis, _gap;
        private readonly float _leftGrow, _rightGrow;
        private int _lastHeight = -1;

        public TwoColumns(Control left, Control right, int leftBasisPx, float leftGrow, int rightBasisPx, float rightGrow, int gapPx)
        {
            _left = left;
            _right = right;
            _leftBasis = Ui.S(leftBasisPx);
            _rightBasis = Ui.S(rightBasisPx);
            _leftGrow = leftGrow;
            _rightGrow = rightGrow;
            _gap = Ui.S(gapPx);
            Margin = Ui.Pad(0);
            BackColor = Color.Transparent;
            AutoSize = true;
            _left.Margin = Ui.Pad(0);
            _right.Margin = Ui.Pad(0);
            Controls.Add(_left);
            Controls.Add(_right);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 && proposedSize.Width < 100000 ? proposedSize.Width : Math.Max(Width, 1);
            Rectangle l, r;
            return new Size(width, Arrange(width, out l, out r));
        }

        private int Arrange(int width, out Rectangle left, out Rectangle right)
        {
            if (width >= _leftBasis + _rightBasis + _gap)
            {
                float free = width - _leftBasis - _rightBasis - _gap;
                int lw = _leftBasis + (int)(free * _leftGrow / (_leftGrow + _rightGrow));
                int rw = width - _gap - lw;
                int lh = _left.GetPreferredSize(new Size(lw, 0)).Height;
                int rh = _right.GetPreferredSize(new Size(rw, 0)).Height;
                left = new Rectangle(0, 0, lw, lh);
                right = new Rectangle(lw + _gap, 0, rw, rh);
                return Math.Max(lh, rh);
            }
            int h1 = _left.GetPreferredSize(new Size(width, 0)).Height;
            int h2 = _right.GetPreferredSize(new Size(width, 0)).Height;
            left = new Rectangle(0, 0, width, h1);
            right = new Rectangle(0, h1 + _gap, width, h2);
            return h1 + _gap + h2;
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            Place();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Place();   // Mono may skip the layout of a control resized while its parent lays out
        }

        private void Place()
        {
            if (_left == null) return;
            Rectangle l, r;
            int height = Arrange(Width, out l, out r);
            if (_left.Bounds != l) _left.Bounds = l;
            if (_right.Bounds != r) _right.Bounds = r;
            if (height != _lastHeight)
            {
                _lastHeight = height;
                if (Parent != null && height != Height) Parent.PerformLayout(this, "PreferredSize");
            }
        }
    }

    /// <summary>
    /// A list row: something fixed on the left (an icon), the text in the middle taking the rest, and buttons or a pill
    /// on the right, all centred on one line. When the window is too narrow for the text, the right part moves under it.
    /// A part is never hidden itself (hide what is inside it): the row then measures the same whether its page is
    /// open or not, and opening the page does not lay it out again.
    /// </summary>
    internal sealed class SplitRow : Panel
    {
        private readonly Control _lead;
        private readonly Control _main;
        private readonly Control _side;
        private readonly int _minMain;
        private readonly int _gap;
        private int _lastHeight = -1;

        public SplitRow(Control lead, Control main, Control side, int minMainPx = 240, int gapPx = 16)
        {
            _lead = lead;
            _main = main;
            _side = side;
            _minMain = Ui.S(minMainPx);
            _gap = Ui.S(gapPx);
            Margin = Ui.Pad(0);
            BackColor = Color.Transparent;
            AutoSize = true;
            foreach (Control c in new[] { lead, main, side })
            {
                if (c == null) continue;
                c.Margin = Ui.Pad(0);
                Controls.Add(c);
            }
        }

        /// <summary>Lines up the left and right parts with the top of the text instead of its middle (rows with long text).</summary>
        public bool AlignTop { get; set; }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 && proposedSize.Width < 100000 ? proposedSize.Width : Math.Max(Width, 1);
            Rectangle a, b, c;
            return new Size(width, Arrange(width, out a, out b, out c));
        }

        private int Arrange(int width, out Rectangle lead, out Rectangle main, out Rectangle side)
        {
            bool hasLead = _lead != null, hasSide = _side != null;
            Size leadSize = hasLead ? _lead.GetPreferredSize(Size.Empty) : Size.Empty;
            Size sideSize = hasSide ? _side.GetPreferredSize(Size.Empty) : Size.Empty;
            int leadWidth = hasLead ? leadSize.Width + _gap : 0;
            int sideWidth = hasSide ? sideSize.Width + _gap : 0;
            int oneLine = width - leadWidth - sideWidth;
            if (oneLine >= _minMain || !hasSide)
            {
                int mainWidth = Math.Max(1, oneLine);
                int mainHeight = _main.GetPreferredSize(new Size(mainWidth, 0)).Height;
                int height = Math.Max(mainHeight, Math.Max(leadSize.Height, sideSize.Height));
                lead = new Rectangle(0, AlignTop ? 0 : (height - leadSize.Height) / 2, leadSize.Width, leadSize.Height);
                main = new Rectangle(leadWidth, (height - mainHeight) / 2, mainWidth, mainHeight);
                int sideTop = AlignTop ? 0 : (height - sideSize.Height) / 2;
                side = new Rectangle(width - sideSize.Width, sideTop, sideSize.Width, sideSize.Height);
                return height;
            }
            int narrowMain = Math.Max(1, width - leadWidth);
            int narrowMainHeight = _main.GetPreferredSize(new Size(narrowMain, 0)).Height;
            int sideWidthNarrow = Math.Min(sideSize.Width, narrowMain);
            int sideHeight = _side.GetPreferredSize(new Size(narrowMain, 0)).Height;
            lead = new Rectangle(0, 0, leadSize.Width, leadSize.Height);
            main = new Rectangle(leadWidth, 0, narrowMain, narrowMainHeight);
            side = new Rectangle(leadWidth, narrowMainHeight + Ui.S(10), Math.Max(sideWidthNarrow, Math.Min(narrowMain, sideSize.Width)), sideHeight);
            return Math.Max(leadSize.Height, side.Bottom);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            Place();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Place();   // Mono may skip the layout of a control resized while its parent lays out
        }

        private void Place()
        {
            if (_main == null) return;
            Rectangle a, b, c;
            int height = Arrange(Width, out a, out b, out c);
            if (_lead != null && _lead.Bounds != a) _lead.Bounds = a;
            if (_main.Bounds != b) _main.Bounds = b;
            if (_side != null && _side.Bounds != c) _side.Bounds = c;
            if (height != _lastHeight)
            {
                _lastHeight = height;
                if (Parent != null && height != Height) Parent.PerformLayout(this, "PreferredSize");
            }
        }
    }
}
