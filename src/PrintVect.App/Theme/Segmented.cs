using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// A row of choices of which one is on (Jobs: Sent / Received; Settings: Light / Dark / System default). Each choice
    /// is a real Button (Tab reaches it, Enter or Space picks it, the arrow keys move along the row).
    /// </summary>
    internal sealed class Segmented : Control
    {
        private readonly List<Choice> _choices = new List<Choice>();
        private int _selected = -1;

        public Segmented(params string[] choices)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Margin = Ui.Pad(0);
            Padding = Ui.Pad(4);
            foreach (string text in choices)
            {
                var choice = new Choice(this, _choices.Count, text);
                _choices.Add(choice);
                Controls.Add(choice);
            }
            Size = GetPreferredSize(Size.Empty);
        }

        /// <summary>Raised when the person picks another choice (not when the code sets SelectedIndex).</summary>
        public event EventHandler SelectedIndexChanged;

        public int SelectedIndex
        {
            get { return _selected; }
            set
            {
                if (value == _selected) return;
                _selected = value;
                foreach (Choice choice in _choices)
                {
                    choice.AccessibleDescription = choice.On ? "selected" : null;
                    choice.Invalidate();
                }
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = Padding.Horizontal, height = 0;
            foreach (Choice choice in _choices)
            {
                Size size = choice.GetPreferredSize(Size.Empty);
                width += size.Width + (width > Padding.Horizontal ? Ui.S(4) : 0);
                height = Math.Max(height, size.Height);
            }
            return new Size(width, height + Padding.Vertical);
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            int x = Padding.Left;
            foreach (Choice choice in _choices)
            {
                Size size = choice.GetPreferredSize(Size.Empty);
                choice.Bounds = new Rectangle(x, Padding.Top, size.Width, Height - Padding.Vertical);
                x += size.Width + Ui.S(4);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Ui.BackOf(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), Ui.S(8)))
            using (var brush = new SolidBrush(Palette.Segment))
            {
                g.FillPath(brush, path);
            }
        }

        private void Pick(int index)
        {
            if (index < 0 || index >= _choices.Count) return;
            _choices[index].Focus();
            if (index == _selected) return;
            SelectedIndex = index;
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }

        private sealed class Choice : Button
        {
            private readonly Segmented _owner;
            private readonly int _index;
            private bool _hover;

            public Choice(Segmented owner, int index, string text)
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                         | ControlStyles.UserPaint, true);
                _owner = owner;
                _index = index;
                Text = text;
                Font = ThemeFonts.TextMedium;
                Cursor = Cursors.Hand;
                Margin = Ui.Pad(0);
                AccessibleRole = AccessibleRole.RadioButton;
                Click += (s, e) => _owner.Pick(_index);
            }

            public bool On
            {
                get { return _owner.SelectedIndex == _index; }
            }

            public override Size GetPreferredSize(Size proposedSize)
            {
                Size text = TextRenderer.MeasureText(Text ?? "", ThemeFonts.TextSemi, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                return new Size(text.Width + Ui.S(28), Ui.S(36));
            }

            protected override bool IsInputKey(Keys keyData)
            {
                return keyData == Keys.Left || keyData == Keys.Right || base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs kevent)
            {
                if (kevent.KeyCode == Keys.Left) { _owner.Pick(_index - 1); kevent.Handled = true; return; }
                if (kevent.KeyCode == Keys.Right) { _owner.Pick(_index + 1); kevent.Handled = true; return; }
                base.OnKeyDown(kevent);
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
                g.Clear(Palette.Segment);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (On)
                {
                    using (GraphicsPath shadow = Ui.RoundRect(new RectangleF(0, 1, Width - 1, Height - 1), Ui.S(6)))
                    using (var brush = new SolidBrush(Palette.Shadow))
                    {
                        g.FillPath(brush, shadow);
                    }
                    using (GraphicsPath path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 2), Ui.S(6)))
                    using (var brush = new SolidBrush(Palette.SegmentSelected))
                    {
                        g.FillPath(brush, path);
                    }
                }
                else if (_hover)
                {
                    using (GraphicsPath path = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 2), Ui.S(6)))
                    using (var brush = new SolidBrush(Color.FromArgb(60, Palette.SegmentSelected)))
                    {
                        g.FillPath(brush, path);
                    }
                }
                if (Focused && ShowFocusCues)
                {
                    using (GraphicsPath ring = Ui.RoundRect(new RectangleF(1.5f, 1.5f, Width - 4f, Height - 5f), Ui.S(5)))
                    using (var pen = new Pen(Palette.Accent, Ui.S(2)))
                    {
                        g.DrawPath(pen, ring);
                    }
                }
                TextRenderer.DrawText(g, Text, On ? ThemeFonts.TextSemi : ThemeFonts.TextMedium, new Rectangle(0, 0, Width, Height - 1), Palette.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }
    }
}
