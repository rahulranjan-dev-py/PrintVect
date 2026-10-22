using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>A slim green progress line for the update download; Marquee runs a moving segment.</summary>
    internal sealed class ThinProgress : Control
    {
        private readonly Timer _timer = new Timer { Interval = 30 };
        private int _value;
        private bool _marquee;
        private float _offset;

        public ThinProgress()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, false);
            Height = Ui.S(6);
            _timer.Tick += (s, e) =>
            {
                _offset = (_offset + 0.015f) % 1.3f;
                Invalidate();
            };
        }

        /// <summary>0 to 100.</summary>
        public int Value
        {
            get { return _value; }
            set
            {
                _value = Math.Max(0, Math.Min(100, value));
                Invalidate();
            }
        }

        public bool Marquee
        {
            get { return _marquee; }
            set
            {
                _marquee = value;
                UpdateTimer();
                Invalidate();
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            UpdateTimer();
        }

        private void UpdateTimer()
        {
            _timer.Enabled = _marquee && Visible;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Ui.BackOf(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float r = Height / 2f;
            using (GraphicsPath track = Ui.RoundRect(new RectangleF(0, 0, Width - 1, Height - 1), r))
            using (var brush = new SolidBrush(Palette.Divider))
            {
                g.FillPath(brush, track);
            }
            float start, length;
            if (_marquee)
            {
                length = Width * 0.3f;
                start = (_offset - 0.3f) * Width;
            }
            else
            {
                start = 0;
                length = Width * _value / 100f;
            }
            if (length < 1)
            {
                return;
            }
            g.SetClip(new RectangleF(0, 0, Width, Height));
            using (GraphicsPath bar = Ui.RoundRect(new RectangleF(start, 0, length, Height - 1), r))
            using (var brush = new SolidBrush(Palette.Accent))
            {
                g.FillPath(brush, bar);
            }
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
