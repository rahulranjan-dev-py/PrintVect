using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    internal enum NoticeKind
    {
        /// <summary>Yellow: something to do (no office PIN, a public network).</summary>
        Warning,
        /// <summary>Blue: good to know (two networks).</summary>
        Info,
        /// <summary>No box: an icon and muted text under a list.</summary>
        Plain
    }

    /// <summary>A small icon in a fixed colour (rebuilt with the window when the colours change).</summary>
    internal sealed class GlyphBox : Control
    {
        private readonly Glyph _glyph;
        private readonly Color _colour;
        private readonly float _stroke;
        private readonly Size _size;

        public GlyphBox(Glyph glyph, int sizePx, Color colour, float stroke = 1.8f)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            _glyph = glyph;
            _colour = colour;
            _stroke = stroke;
            _size = new Size(Ui.S(sizePx), Ui.S(sizePx));
            Size = _size;
            Margin = Ui.Pad(0);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            return _size;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Ui.BackOf(Parent));
            Icons.Draw(e.Graphics, _glyph, new RectangleF(0, 0, Width, Height), _colour, _stroke);
        }
    }

    /// <summary>
    /// A rounded note with an icon, wrapping text and an optional action on the right ("Set a PIN", "Make it private").
    /// </summary>
    internal sealed class Notice : Panel
    {
        private readonly NoticeKind _kind;
        private readonly Label _text;
        private readonly SplitRow _row;

        public Notice(NoticeKind kind, Glyph glyph, string text, Control action = null)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            _kind = kind;
            Color fore = kind == NoticeKind.Warning ? Palette.WarningText : kind == NoticeKind.Info ? Palette.InfoText : Palette.Muted;
            Color icon = kind == NoticeKind.Warning ? Palette.WarningText : kind == NoticeKind.Info ? Palette.InfoIcon : Palette.Muted;
            BackColor = Back;
            AutoSize = true;   // a list sizes it by GetPreferredSize, not by its current height
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = kind == NoticeKind.Plain ? Ui.Pad(4, 0, 4, 0) : Ui.Pad(14, 12, 14, 12);
            Margin = Ui.Pad(0);

            var glyphBox = new GlyphBox(glyph, 18, icon);
            _text = Make.Wrap(text, ThemeFonts.Secondary, fore, Ui.Pad(0));
            _row = new SplitRow(glyphBox, _text, action, 220, 10) { BackColor = Back };
            _row.AlignTop = action == null;
            Controls.Add(_row);
        }

        private Color Back
        {
            get { return _kind == NoticeKind.Warning ? Palette.WarningBg : _kind == NoticeKind.Info ? Palette.InfoBg : Color.Transparent; }
        }

        public string Message
        {
            get { return _text.Text; }
            set { _text.Text = value ?? ""; }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            if (_row == null) return base.GetPreferredSize(proposedSize);   // still being built
            int width = proposedSize.Width > 0 && proposedSize.Width < 100000 ? proposedSize.Width : Math.Max(Width, 1);
            Size inner = _row.GetPreferredSize(new Size(Math.Max(1, width - Padding.Horizontal), 0));
            return new Size(width, inner.Height + Padding.Vertical);
        }

        /// <summary>The text wraps differently once the note has its real width: the list around it is told its new height.</summary>
        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            PlaceRow();
        }

        /// <summary>
        /// Also on every resize: a note that appears after being hidden is widened while its parent lays out, and Mono
        /// then lays out nothing inside it (its row kept a width of a few pixels).
        /// </summary>
        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            PlaceRow();
        }

        private void PlaceRow()
        {
            if (_row == null) return;
            var bounds = new Rectangle(Padding.Left, Padding.Top, Math.Max(1, Width - Padding.Horizontal), Math.Max(1, Height - Padding.Vertical));
            if (_row.Bounds != bounds) _row.Bounds = bounds;
            int height = GetPreferredSize(new Size(Width, 0)).Height;
            if (height != Height && Parent != null) Parent.PerformLayout(this, "PreferredSize");
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color outside = Ui.BackOf(Parent);
            e.Graphics.Clear(outside);
            if (_kind == NoticeKind.Plain) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), Ui.S(8)))
            using (var brush = new SolidBrush(Back))
            {
                e.Graphics.FillPath(brush, path);
            }
        }
    }
}
