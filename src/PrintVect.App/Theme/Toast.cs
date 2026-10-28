using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// A short message at the bottom right of the page ("Copied ACCOUNTS-PC", "Test page sent") that goes away by itself
    /// after a few seconds. It replaces the old status bar, which the redesign does not have; a click closes it.
    /// </summary>
    internal sealed class Toast : Control
    {
        private const int MaxWidthPx = 460;
        private readonly Timer _timer;

        public Toast(int seconds)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Visible = false;
            Font = ThemeFonts.TextMedium;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.Alert;
            _timer = new Timer { Interval = seconds * 1000 };
            _timer.Tick += (s, e) => Hide();
            Click += (s, e) => Hide();
        }

        public void Say(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }
            Text = text;
            AccessibleName = text;
            Size textSize = TextRenderer.MeasureText(text, Font, new Size(Ui.S(MaxWidthPx) - Ui.S(32), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            Size = new Size(Math.Min(Ui.S(MaxWidthPx), textSize.Width + Ui.S(34)), textSize.Height + Ui.S(22));
            Place();
            using (GraphicsPath path = Ui.RoundRect(new RectangleF(0, 0, Width, Height), Ui.S(8)))
            {
                Region = new Region(path);
            }
            Visible = true;
            BringToFront();
            Invalidate();
            _timer.Stop();
            _timer.Start();
        }

        public new void Hide()
        {
            _timer.Stop();
            Visible = false;
        }

        /// <summary>Bottom right of the page, 24 px from both edges; again whenever the window is resized.</summary>
        public void Place()
        {
            if (Parent == null) return;
            Location = new Point(Math.Max(Ui.S(8), Parent.ClientSize.Width - Width - Ui.S(24)),
                Math.Max(Ui.S(8), Parent.ClientSize.Height - Height - Ui.S(24)));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Palette.ToastBg);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Ui.S(16), Ui.S(11), Width - Ui.S(32), Height - Ui.S(22)), Palette.ToastText,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
