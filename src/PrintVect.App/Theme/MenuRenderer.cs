using System.Drawing;
using System.Windows.Forms;

namespace PrintVect.App.Theme
{
    /// <summary>The right-click menus (tray icon, job lists) in the colours of the theme in use.</summary>
    internal sealed class MenuRenderer : ToolStripProfessionalRenderer
    {
        public MenuRenderer() : base(new Colours())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Palette.Text : Palette.Disabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Palette.Text;
            base.OnRenderArrow(e);
        }

        /// <summary>A menu built with the colours of the theme in use.</summary>
        public static ContextMenuStrip Create()
        {
            return new ContextMenuStrip { Renderer = new MenuRenderer(), Font = ThemeFonts.Text, ShowImageMargin = false, BackColor = Palette.Card };
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
