using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// The design's on/off switch (44 x 24): green track with a white knob when on, an outlined track with a grey knob
    /// when off. A CheckBox underneath (Checked, CheckedChanged, Space to toggle). In a settings row the text sits in
    /// the row and the switch has none (its AccessibleName says what it switches).
    /// </summary>
    internal sealed class ToggleSwitch : CheckBox
    {
        private const int TrackWidth = 44;
        private const int TrackHeight = 24;
        private const int Gap = 12;

        /// <summary>Where the text starts (design pixels), for a note lined up under it.</summary>
        public const int TextIndentPx = TrackWidth + Gap;

        public ToggleSwitch(string text = "")
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                     | ControlStyles.UserPaint, true);
            Text = text;
            Font = ThemeFonts.TextSemi;
            ForeColor = Palette.Text;
            AutoSize = true;
            Cursor = Cursors.Hand;
            Margin = Ui.Pad(0, 4, 0, 4);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            Size text = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(Text, Font);
            int width = Ui.S(TrackWidth) + (text.Width > 0 ? Ui.S(Gap) + text.Width + Ui.S(4) : 0);
            return new Size(width, Math.Max(Ui.S(TrackHeight + 4), text.Height + Ui.S(4)));
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            Invalidate();
            base.OnCheckedChanged(e);
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.Clear(Ui.BackOf(Parent));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = Ui.S(TrackWidth), h = Ui.S(TrackHeight);
            int top = Math.Max(0, (Height - h) / 2);
            var track = new RectangleF(0.5f, top + 0.5f, w - 1f, h - 1f);
            Color fill = !Enabled ? Palette.DisabledBg : Checked ? Palette.Accent : Palette.SwitchOffTrack;
            Color edge = !Enabled ? Palette.DisabledBorder : Checked ? Palette.Accent : Palette.SwitchOffBorder;
            using (GraphicsPath path = Ui.RoundRect(track, h / 2f))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(edge, 1f))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            int knob = Ui.S(16);
            float x = Checked ? w - Ui.S(4) - knob : Ui.S(4);
            Color knobColour = !Enabled ? Palette.Disabled : Checked ? Palette.Knob : Palette.KnobOff;
            using (var brush = new SolidBrush(knobColour))
            {
                g.FillEllipse(brush, x, top + (h - knob) / 2f, knob, knob);
            }
            if (Focused && ShowFocusCues)
            {
                using (GraphicsPath ring = Ui.RoundRect(new RectangleF(1f, top + 1f, w - 2f, h - 2f), h / 2f))
                using (var pen = new Pen(Palette.Text, 1f) { DashStyle = DashStyle.Dot })
                {
                    g.DrawPath(pen, ring);
                }
            }

            if (string.IsNullOrEmpty(Text))
            {
                return;
            }
            var textRect = new Rectangle(w + Ui.S(Gap), 0, Width - w - Ui.S(Gap), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? ForeColor : Palette.Disabled,
                TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
