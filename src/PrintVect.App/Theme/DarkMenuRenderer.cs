using System.Drawing;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>The tray icon's right-click menu in the dark colours.</summary>
    internal sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new Colours())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Palette.Text : Palette.Muted;
            base.OnRenderItemText(e);
        }

        private sealed class Colours : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Palette.Card; } }
            public override Color ImageMarginGradientBegin { get { return Palette.Card; } }
            public override Color ImageMarginGradientMiddle { get { return Palette.Card; } }
            public override Color ImageMarginGradientEnd { get { return Palette.Card; } }
            public override Color MenuBorder { get { return Palette.Border; } }
            public override Color MenuItemBorder { get { return Palette.Selection; } }
            public override Color MenuItemSelected { get { return Palette.Selection; } }
            public override Color MenuItemSelectedGradientBegin { get { return Palette.Selection; } }
            public override Color MenuItemSelectedGradientEnd { get { return Palette.Selection; } }
            public override Color MenuItemPressedGradientBegin { get { return Palette.Selection; } }
            public override Color MenuItemPressedGradientEnd { get { return Palette.Selection; } }
            public override Color SeparatorDark { get { return Palette.Border; } }
            public override Color SeparatorLight { get { return Palette.Border; } }
        }
    }
}
