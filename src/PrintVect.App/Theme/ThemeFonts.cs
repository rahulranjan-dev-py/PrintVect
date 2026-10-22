using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using PrintVect.Core.Logging;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// The fonts of the design (0.5.0): Noto Sans for text, JetBrains Mono for PC names and addresses, both shipped in
    /// the Fonts folder next to the program and loaded for this process only (no system-wide install). A missing file
    /// falls back to Segoe UI or Consolas. Sizes are the design's pixels at 100 %, turned into points.
    /// </summary>
    internal static class ThemeFonts
    {
        private const string TextFamily = "Noto Sans";
        private const string MediumFamily = "Noto Sans Medium";
        private const string SemiFamily = "Noto Sans SemiBold";
        private const string MonoFamily = "JetBrains Mono";
        private const string MonoSemiFamily = "JetBrains Mono SemiBold";

        private static readonly string[] Files =
        {
            @"NotoSans\NotoSans-Regular.ttf", @"NotoSans\NotoSans-Medium.ttf", @"NotoSans\NotoSans-SemiBold.ttf",
            @"NotoSans\NotoSans-Bold.ttf", @"JetBrainsMono\JetBrainsMono-Regular.ttf", @"JetBrainsMono\JetBrainsMono-SemiBold.ttf"
        };

        private static PrivateFontCollection _collection;

        /// <summary>Base text, 14 px.</summary>
        public static Font Text { get; private set; }
        /// <summary>14 px medium: menu entries and outlined buttons.</summary>
        public static Font TextMedium { get; private set; }
        /// <summary>14 px semibold: names in a list, field names, main buttons.</summary>
        public static Font TextSemi { get; private set; }
        /// <summary>Secondary text, 13 px.</summary>
        public static Font Secondary { get; private set; }
        /// <summary>13 px semibold: the label above a text box.</summary>
        public static Font SecondarySemi { get; private set; }
        /// <summary>Pills and column headings, 12.5 px semibold.</summary>
        public static Font Small { get; private set; }
        /// <summary>12 px: the version line, small capitals labels (semibold).</summary>
        public static Font Tiny { get; private set; }
        public static Font TinySemi { get; private set; }
        /// <summary>Page titles, 26 px semibold.</summary>
        public static Font PageTitle { get; private set; }
        /// <summary>Section titles above a card, 16 px semibold.</summary>
        public static Font SectionTitle { get; private set; }
        /// <summary>Titles inside a card, 15 px semibold.</summary>
        public static Font CardTitle { get; private set; }
        /// <summary>A bigger card or window title, 17 px / 20 px semibold.</summary>
        public static Font Heading { get; private set; }
        public static Font DialogTitle { get; private set; }
        /// <summary>A printer's name on its card, 15.5 px semibold.</summary>
        public static Font RowTitle { get; private set; }
        /// <summary>"PrintVect" next to the logo, 17 px bold.</summary>
        public static Font Brand { get; private set; }
        /// <summary>Big numbers, 28 px semibold.</summary>
        public static Font Number { get; private set; }
        /// <summary>PC names and addresses: 13 px, 14 px (typed), semibold 13 / 15 / 16 px.</summary>
        public static Font Mono { get; private set; }
        public static Font MonoText { get; private set; }
        public static Font MonoSemi { get; private set; }
        public static Font MonoName { get; private set; }
        public static Font MonoBig { get; private set; }
        /// <summary>The full Diagnostics report, 12.5 px.</summary>
        public static Font Report { get; private set; }

        /// <summary>What was loaded, for the log and the Diagnostics text.</summary>
        public static string Description { get; private set; }

        public static void Load(string programDir)
        {
            _collection = new PrivateFontCollection();
            int loaded = 0;
            foreach (string relative in Files)
            {
                string path = Path.Combine(programDir, "Fonts", relative.Replace('\\', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    Log.Warn("Font file missing, using the Windows font instead: " + path);
                    continue;
                }
                try
                {
                    _collection.AddFontFile(path);
                    NativeTheme.RegisterPrivateFont(path);
                    loaded++;
                }
                catch (Exception ex)
                {
                    Log.Warn("Font file could not be loaded, using the Windows font instead: " + path + ": " + ex.Message);
                }
            }

            FontFamily text = Find(TextFamily) ?? Fallback("Segoe UI") ?? FontFamily.GenericSansSerif;
            FontFamily medium = Find(MediumFamily) ?? Fallback("Segoe UI") ?? text;
            FontFamily semi = Find(SemiFamily) ?? Fallback("Segoe UI Semibold") ?? text;
            FontFamily mono = Find(MonoFamily) ?? Fallback("Consolas") ?? FontFamily.GenericMonospace;
            FontFamily monoSemi = Find(MonoSemiFamily);
            FontStyle monoSemiStyle = FontStyle.Regular;
            if (monoSemi == null)
            {
                monoSemi = mono;
                monoSemiStyle = FontStyle.Bold;   // Consolas has no semibold: bold reads the same at these sizes
            }
            // Without the bundled SemiBold, "semibold" text falls back to bold so headings still stand out.
            FontStyle semiStyle = string.Equals(semi.Name, text.Name, StringComparison.OrdinalIgnoreCase) ? FontStyle.Bold : FontStyle.Regular;

            Text = Make(text, 14f, FontStyle.Regular);
            TextMedium = Make(medium, 14f, FontStyle.Regular);
            TextSemi = Make(semi, 14f, semiStyle);
            Secondary = Make(text, 13f, FontStyle.Regular);
            SecondarySemi = Make(semi, 13f, semiStyle);
            Small = Make(semi, 12.5f, semiStyle);
            Tiny = Make(text, 12f, FontStyle.Regular);
            TinySemi = Make(semi, 12f, semiStyle);
            PageTitle = Make(semi, 26f, semiStyle);
            SectionTitle = Make(semi, 16f, semiStyle);
            CardTitle = Make(semi, 15f, semiStyle);
            Heading = Make(semi, 17f, semiStyle);
            DialogTitle = Make(semi, 20f, semiStyle);
            RowTitle = Make(semi, 15.5f, semiStyle);
            Brand = Make(text, 17f, FontStyle.Bold);
            Number = Make(semi, 28f, semiStyle);
            Mono = Make(mono, 13f, FontStyle.Regular);
            MonoText = Make(mono, 14f, FontStyle.Regular);
            MonoSemi = Make(monoSemi, 13f, monoSemiStyle);
            MonoName = Make(monoSemi, 15f, monoSemiStyle);
            MonoBig = Make(monoSemi, 16f, monoSemiStyle);
            Report = Make(mono, 12.5f, FontStyle.Regular);

            Description = string.Format("{0} of {1} font files loaded; text {2}, names {3}", loaded, Files.Length, text.Name, mono.Name);
            Log.Info("Fonts: " + Description + ".");
        }

        private static FontFamily Find(string name)
        {
            foreach (FontFamily family in _collection.Families)
            {
                if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    if (Usable(family))
                    {
                        return family;
                    }
                    Log.Warn("Font " + name + " loaded but cannot be measured; using the installed one or the Windows font.");
                    return Fallback(name);
                }
            }
            return null;
        }

        /// <summary>A family that GDI+ can build and measure a font from (a damaged file would fail here, not on screen).</summary>
        private static bool Usable(FontFamily family)
        {
            try
            {
                FontStyle style = family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold;
                using (var font = new Font(family, 10f, style, GraphicsUnit.Point))
                {
                    return font.Height > 0;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private static FontFamily Fallback(string name)
        {
            try
            {
                var family = new FontFamily(name);
                return Usable(family) ? family : null;
            }
            catch (ArgumentException)
            {
                return null;   // not installed (Windows 7 has no Segoe UI Semibold): the caller picks another
            }
        }

        private static Font Make(FontFamily family, float px, FontStyle style)
        {
            if (!family.IsStyleAvailable(style))
            {
                style = family.IsStyleAvailable(FontStyle.Regular) ? FontStyle.Regular : FontStyle.Bold;
            }
            return new Font(family, px * 0.75f, style, GraphicsUnit.Point);
        }
    }
}
