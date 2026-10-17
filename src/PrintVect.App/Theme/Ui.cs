using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// Sizes of the design in pixels at 100 % (96 DPI), scaled to the screen. PrintVect is system-DPI aware, so the
    /// factor is fixed for the session; fonts are given in points and scale by themselves.
    /// </summary>
    internal static class Ui
    {
        private static float _scale;

        public static float Scale
        {
            get
            {
                if (_scale <= 0)
                {
                    try
                    {
                        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                        {
                            _scale = Math.Max(1f, g.DpiX / 96f);
                        }
                    }
                    catch (Exception)
                    {
                        _scale = 1f;   // no screen to ask (never on a desktop session); 100 % is a safe size
                    }
                }
                return _scale;
            }
        }

        /// <summary>A length of the design (px at 100 %) on this screen.</summary>
        public static int S(int px)
        {
            return (int)Math.Round(px * Scale);
        }

        public static System.Windows.Forms.Padding Pad(int all)
        {
            return new System.Windows.Forms.Padding(S(all));
        }

        public static System.Windows.Forms.Padding Pad(int left, int top, int right, int bottom)
        {
            return new System.Windows.Forms.Padding(S(left), S(top), S(right), S(bottom));
        }

        /// <summary>
        /// The colour actually behind a control: the first parent with a solid background. Painted controls clear to
        /// it, because a transparent BackColor cleared directly shows black.
        /// </summary>
        public static Color BackOf(System.Windows.Forms.Control control)
        {
            for (System.Windows.Forms.Control c = control; c != null; c = c.Parent)
            {
                if (c.BackColor.A == 255)
                {
                    return c.BackColor;
                }
            }
            return Palette.Background;
        }

        /// <summary>
        /// Gives every control under <paramref name="root"/> that was built with a transparent background the solid colour
        /// behind it (card or page). Layout panels then paint themselves instead of asking their parents, which is faster
        /// and leaves no stale pixels when the window is resized.
        /// </summary>
        public static void Solidify(System.Windows.Forms.Control root)
        {
            foreach (System.Windows.Forms.Control child in root.Controls)
            {
                if (child.BackColor.A < 255)
                {
                    child.BackColor = BackOf(root);
                }
                Solidify(child);
            }
        }

        /// <summary>A rectangle with round corners for cards (12 px), buttons (8 px) and pills (half the height).</summary>
        public static GraphicsPath RoundRect(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (d <= 0.5f)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
