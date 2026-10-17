using System.Drawing;

namespace PrintVect.App.Theme
{
    /// <summary>The Graphite colours of the owner's design (docs/design/design-spec.md, "Colours").</summary>
    internal static class Palette
    {
        public static readonly Color Background = Hex(0x111418);
        public static readonly Color TopBar = Hex(0x151920);
        public static readonly Color Card = Hex(0x181C22);
        public static readonly Color Border = Hex(0x262D37);
        public static readonly Color Outline = Hex(0x3A4350);
        public static readonly Color Text = Hex(0xEEF1F5);
        public static readonly Color Muted = Hex(0x9BA6B4);

        public static readonly Color Accent = Hex(0x3FCF8E);
        public static readonly Color OnAccent = Hex(0x06150E);
        public static readonly Color Warning = Hex(0xF0B03F);
        public static readonly Color OnWarning = Hex(0x1A1204);

        public static readonly Color ConnectedPill = Hex(0x17352A);
        public static readonly Color ConnectedText = Hex(0x6FE0AE);
        public static readonly Color OfflinePill = Hex(0x232931);
        public static readonly Color OfflineText = Hex(0xAEB8C4);
        public static readonly Color Knob = Hex(0xFFFFFF);

        // Not in the mock-ups, which show no failures: a muted red for failed jobs, and the hover / selection
        // shades of the card colour.
        public static readonly Color Error = Hex(0xF27A72);
        public static readonly Color Hover = Hex(0x20262E);
        public static readonly Color Selection = Hex(0x232B36);

        private static Color Hex(int rgb)
        {
            return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
        }
    }
}
