# Bundled fonts

Both families are licensed under the SIL Open Font License 1.1; the licence text sits beside each
family and ships with the program (installer and build artifact).

| Folder | Files | Source |
|--------|-------|--------|
| `Manrope/` | `Manrope-Regular.ttf`, `-SemiBold.ttf`, `-Bold.ttf` (shipped); `Manrope-VariableFont_wght.ttf` (source, not shipped) | github.com/google/fonts, `ofl/manrope`; the three static weights are made from the variable font by `tools/make_fonts.py` |
| `JetBrainsMono/` | `JetBrainsMono-Regular.ttf`, `-Medium.ttf`, `-Bold.ttf` | github.com/JetBrains/JetBrainsMono release 2.304, unmodified |

Why static Manrope files: GDI+ (System.Drawing) only sees the default instance of a variable font, which
for Manrope is ExtraLight. Manrope's licence names no Reserved Font Name, so the instances keep the name.

The app loads them for its own process only at start-up (`Theme/ThemeFonts.cs`: a PrivateFontCollection
for GDI+ and AddFontResourceEx with FR_PRIVATE for GDI), with Segoe UI and Consolas as fallbacks when a
file is missing or cannot be used.
