using System;
using System.Drawing;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// Text that wraps to the width it is given and makes itself as tall as the wrapped lines. An auto-sized Label
    /// measures itself on one line, which cuts the end off long messages inside the cards; this one is anchored
    /// left and right, so the layout sets its width and the label sets its height.
    /// </summary>
    internal sealed class WrapLabel : Label
    {
        public WrapLabel()
        {
            AutoSize = false;
            UseMnemonic = false;
            BackColor = Color.Transparent;
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int width = proposedSize.Width > 0 && proposedSize.Width < 100000 ? proposedSize.Width : Math.Max(Width, 1);
            return new Size(width, HeightFor(width));
        }

        private int HeightFor(int width)
        {
            if (string.IsNullOrEmpty(Text))
            {
                return 0;
            }
            Size text = TextRenderer.MeasureText(Text, Font, new Size(Math.Max(1, width - Padding.Horizontal), int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix);
            return text.Height + Padding.Vertical;
        }

        private void FitHeight()
        {
            int height = HeightFor(Width);
            if (height != Height)
            {
                Height = height;
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            FitHeight();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            FitHeight();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            FitHeight();
        }
    }
}
