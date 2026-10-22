using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// A one-line text box with the design's outline and 6 px corners (green while typing). A plain TextBox cannot
    /// have a themed border, so this draws the frame and hosts a borderless TextBox inside it.
    /// </summary>
    internal sealed class InputBox : Panel
    {
        private readonly int _heightPx;
        private readonly int _widthPx;

        public InputBox(int widthPx, int heightPx = 44, Font font = null)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            _heightPx = heightPx;
            _widthPx = widthPx;
            BackColor = Palette.InputBg;
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Palette.InputBg,
                ForeColor = Palette.Text,
                Font = font ?? ThemeFonts.MonoText,
                Dock = DockStyle.Fill
            };
            Size = new Size(Ui.S(widthPx), Ui.S(heightPx));
            int vertical = (Ui.S(heightPx) - Box.PreferredHeight) / 2;
            Padding = new Padding(Ui.S(12), vertical, Ui.S(12), 0);
            Margin = Ui.Pad(0);
            Controls.Add(Box);
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Click += (s, e) => Box.Focus();
        }

        public TextBox Box { get; private set; }

        /// <summary>The outline in the softer list colour (the name boxes of the printer list).</summary>
        public bool Soft { get; set; }

        public override Size GetPreferredSize(Size proposedSize)
        {
            return new Size(Ui.S(_widthPx), Ui.S(_heightPx));
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Ui.BackOf(Parent));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            bool focused = Box.Focused;
            Color edge = !Enabled ? Palette.DisabledBorder : focused ? Palette.Accent : Soft ? Palette.ButtonBorder : Palette.InputBorder;
            using (GraphicsPath path = Ui.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Ui.S(6)))
            using (var fill = new SolidBrush(Enabled ? Palette.InputBg : Palette.DisabledBg))
            using (var pen = new Pen(edge, focused ? Ui.S(2) : 1f))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        protected override void OnEnabledChanged(System.EventArgs e)
        {
            base.OnEnabledChanged(e);
            Box.BackColor = Enabled ? Palette.InputBg : Palette.DisabledBg;
            Invalidate();
        }
    }
}
