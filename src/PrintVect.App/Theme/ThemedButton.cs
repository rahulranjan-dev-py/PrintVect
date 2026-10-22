using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    internal enum ButtonKind
    {
        /// <summary>Green with white (light) or dark (dark) text: the one main action of a card.</summary>
        Primary,
        /// <summary>Outlined: every other action.</summary>
        Secondary,
        /// <summary>No outline until the mouse is on it: small actions beside a heading.</summary>
        Ghost,
        /// <summary>Red text, no outline: Remove.</summary>
        Danger
    }

    /// <summary>
    /// A button of the design (6 px corners, 44 px tall, 36 px when compact), with an optional line icon before the
    /// text. Still a real Button underneath, so the keyboard, Enter / Space, the focus order and screen readers work.
    /// </summary>
    internal sealed class ThemedButton : Button
    {
        private bool _hover;
        private bool _pressed;
        private Glyph _glyph;

        public ThemedButton(string text, ButtonKind kind, Glyph glyph = Glyph.None)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            Kind = kind;
            _glyph = glyph;
            Text = text;
            Font = kind == ButtonKind.Primary ? ThemeFonts.TextSemi : ThemeFonts.TextMedium;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Margin = Ui.Pad(0, 0, 8, 0);
            Cursor = Cursors.Hand;
            MinimumHeight = 44;
        }

        public ButtonKind Kind { get; set; }

        public Glyph Glyph
        {
            get { return _glyph; }
            set
            {
                _glyph = value;
                Invalidate();
            }
        }

        /// <summary>Design pixels at 100 %.</summary>
        public int MinimumHeight { get; set; }

        /// <summary>36 px tall, for buttons inside a list row.</summary>
        public bool Compact
        {
            get { return MinimumHeight < 40; }
            set { MinimumHeight = value ? 36 : 44; }
        }

        /// <summary>Only the icon is shown (the text stays the accessible name and the tooltip).</summary>
        public bool IconOnly { get; set; }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int height = Ui.S(MinimumHeight);
            if (IconOnly)
            {
                return new Size(height, height);
            }
            Size text = TextRenderer.MeasureText(Text ?? "", Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            int icon = _glyph == Glyph.None ? 0 : Ui.S(18 + 8);
            int pad = Ui.S(Kind == ButtonKind.Primary ? 16 : 14);
            return new Size(text.Width + icon + 2 * pad, Math.Max(height, text.Height + Ui.S(12)));
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (AutoSize && Parent != null) Parent.PerformLayout(this, "Text");
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
            if (!Enabled)
            {
                // A button disabled under the mouse gets no MouseLeave; drop the hover look with it.
                _hover = false;
                _pressed = false;
            }
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
            using (GraphicsPath path = Ui.RoundRect(bounds, Ui.S(6)))
            {
                if (fill.A > 0)
                {
                    using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
                }
                if (border.A > 0)
                {
                    using (var pen = new Pen(border, 1f)) g.DrawPath(pen, path);
                }
            }
            if (Focused && ShowFocusCues)
            {
                var ring = new RectangleF(1.5f, 1.5f, Width - 3.5f, Height - 3.5f);
                using (GraphicsPath path = Ui.RoundRect(ring, Ui.S(5)))
                using (var pen = new Pen(Kind == ButtonKind.Primary && Enabled ? Palette.OnAccent : Palette.Accent, Ui.S(2)))
                {
                    g.DrawPath(pen, path);
                }
            }

            int iconSize = Ui.S(18);
            if (IconOnly)
            {
                Icons.Draw(g, _glyph, new RectangleF((Width - iconSize) / 2f, (Height - iconSize) / 2f, iconSize, iconSize), text);
                return;
            }
            Size measured = TextRenderer.MeasureText(Text ?? "", Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            int content = measured.Width + (_glyph == Glyph.None ? 0 : iconSize + Ui.S(8));
            int x = Math.Max(Ui.S(8), (Width - content) / 2);
            if (_glyph != Glyph.None)
            {
                Icons.Draw(g, _glyph, new RectangleF(x, (Height - iconSize) / 2f, iconSize, iconSize), text);
                x += iconSize + Ui.S(8);
            }
            var textRect = new Rectangle(x, 0, Math.Max(1, Width - x - Ui.S(6)), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
                | TextFormatFlags.NoPrefix);
        }

        private void Colours(out Color fill, out Color text, out Color border)
        {
            if (!Enabled)
            {
                bool flat = Kind == ButtonKind.Ghost || Kind == ButtonKind.Danger;
                fill = flat ? Color.Transparent : Palette.DisabledBg;
                border = flat ? Color.Transparent : Palette.DisabledBorder;
                text = Palette.Disabled;
                return;
            }
            switch (Kind)
            {
                case ButtonKind.Primary:
                    border = Color.Transparent;
                    fill = _pressed ? Palette.AccentPressed : _hover ? Palette.AccentHover : Palette.Accent;
                    text = Palette.OnAccent;
                    return;
                case ButtonKind.Ghost:
                    border = Color.Transparent;
                    fill = _pressed ? Palette.Pressed : _hover ? Palette.Hover : Color.Transparent;
                    text = Palette.Text;
                    return;
                case ButtonKind.Danger:
                    border = Color.Transparent;
                    fill = _pressed || _hover ? Palette.DangerBg : Color.Transparent;
                    text = Palette.DangerText;
                    return;
                default:
                    fill = _pressed ? Palette.Pressed : _hover ? Palette.Hover : Palette.Card;
                    border = Palette.ButtonBorder;
                    text = Palette.Text;
                    return;
            }
        }
    }
}
