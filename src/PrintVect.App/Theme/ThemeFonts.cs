using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using PrintVect.Core.Logging;

namespace PrintVect.App.Theme
{
    /// <summary>
    /// The fonts of the design: Manrope for text, JetBrains Mono for PC names and addresses, both shipped in the
    /// Fonts folder next to the program and loaded for this process only (no system-wide install). A missing file
    /// falls back to Segoe UI or Consolas. Sizes are the design's pixels at 100 %, turned into points.
    /// </summary>
    internal static class ThemeFonts
    {
        private const string TextFamily = "Manrope";
        private const string TextSemiFamily = "Manrope SemiBold";
        private const string MonoFamily = "JetBrains Mono";

        private static readonly string[] Files =
        {
            @"Manrope\Manrope-Regular.ttf", @"Manrope\Manrope-SemiBold.ttf", @"Manrope\Manrope-Bold.ttf",
            @"JetBrainsMono\JetBrainsMono-Regular.ttf", @"JetBrainsMono\JetBrainsMono-Bold.ttf"
        };

        private static PrivateFontCollection _collection;
        private static FontFamily _text;
        private static FontFamily _textSemi;
        private static FontFamily _mono;

        /// <summary>Base text, 14 px.</summary>
        public static Font Text { get; private set; }
        /// <summary>Base text, 14 px, semibold: buttons, tabs, field names.</summary>
        public static Font TextSemi { get; private set; }
        /// <summary>Secondary text, 13 px.</summary>
        public static Font Secondary { get; private set; }
        /// <summary>Pills and column headings, 12 px semibold.</summary>
        public static Font Small { get; private set; }
        /// <summary>Card titles, 16 px bold.</summary>
        public static Font CardTitle { get; private set; }
        /// <summary>Small labels, 11 px bold (shown in capitals).</summary>
        public static Font Label { get; private set; }
        /// <summary>A PC or printer name in large, 24 px bold.</summary>
        public static Font Big { get; private set; }
        /// <summary>Big numbers, 28 px bold.</summary>
        public static Font Number { get; private set; }
        /// <summary>PC names and addresses, 13 px.</summary>
        public static Font Mono { get; private set; }
        /// <summary>A PC name in large, 24 px bold monospace.</summary>
        public static Font MonoBig { get; private set; }

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

            _text = Find(TextFamily) ?? Fallback("Segoe UI");
            _textSemi = Find(TextSemiFamily) ?? Fallback("Segoe UI Semibold") ?? _text;
            _mono = Find(MonoFamily) ?? Fallback("Consolas") ?? FontFamily.GenericMonospace;

            Text = Make(_text, 14, FontStyle.Regular);
            TextSemi = Make(_textSemi, 14, FontStyle.Regular);
            Secondary = Make(_text, 13, FontStyle.Regular);
            Small = Make(_textSemi, 12, FontStyle.Regular);
            CardTitle = Make(_text, 16, FontStyle.Bold);
            Label = Make(_text, 11, FontStyle.Bold);
            Big = Make(_text, 24, FontStyle.Bold);
            Number = Make(_text, 28, FontStyle.Bold);
            Mono = Make(_mono, 13, FontStyle.Regular);
            MonoBig = Make(_mono, 24, FontStyle.Bold);

            Description = string.Format("{0} of {1} font files loaded; text {2}, names {3}", loaded, Files.Length,
                _text.Name, _mono.Name);
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
                using (var font = new Font(family, 10f, FontStyle.Regular, GraphicsUnit.Point))
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

        private static Font Make(FontFamily family, int px, FontStyle style)
        {
            FontFamily use = family ?? FontFamily.GenericSansSerif;
            if (!use.IsStyleAvailable(style))
            {
                style = FontStyle.Regular;
            }
            return new Font(use, px * 0.75f, style, GraphicsUnit.Point);
        }
    }
}
