using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// A one-line text box with the design's outline and 8 px corners. A plain TextBox cannot have a dark border,
    /// so this draws the frame and hosts a borderless TextBox inside it.
    /// </summary>
    internal sealed class InputBox : Panel
    {
        public InputBox(int widthPx)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            BackColor = Palette.Background;
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Palette.Background,
                ForeColor = Palette.Text,
                Font = ThemeFonts.Mono,
                Dock = DockStyle.Fill
            };
            Size = new Size(Ui.S(widthPx), Ui.S(44));
            int vertical = (Ui.S(44) - Box.PreferredHeight) / 2;
            Padding = new Padding(Ui.S(12), vertical, Ui.S(12), 0);
            Margin = Ui.Pad(0, 0, 8, 0);
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Click += (s, e) => Box.Focus();
        }

        public TextBox Box { get; private set; }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Ui.BackOf(Parent));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Ui.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Ui.S(8)))
            using (var fill = new SolidBrush(Palette.Background))
            using (var pen = new Pen(Box.Focused ? Palette.Accent : Palette.Outline, 1f))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
        }
    }
}
