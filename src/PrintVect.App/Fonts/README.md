# Bundled fonts

Both families are licensed under the SIL Open Font License 1.1; the licence text sits beside each
family and ships with the program (installer and build artifact).

| Folder | Files | Source |
|--------|-------|--------|
| `NotoSans/` | `NotoSans-Regular.ttf`, `-Medium.ttf`, `-SemiBold.ttf`, `-Bold.ttf` | github.com/google/fonts, `ofl/notosans/NotoSans[wdth,wght].ttf` (SHA-256 in the script); the four static weights are made from the variable font by `tools/make_fonts.py`, cut down to Latin letters, punctuation, currency signs and arrows |
| `JetBrainsMono/` | `JetBrainsMono-Regular.ttf`, `-SemiBold.ttf` | github.com/JetBrains/JetBrainsMono release 2.304, unmodified |

Why static Noto Sans files: GDI+ (System.Drawing) only sees the default instance of a variable font. Noto Sans's
licence names no Reserved Font Name, so the instances keep the name. Characters outside the cut (a Hindi document
name, for example) are drawn by Windows with its own fonts.

The app loads them for its own process only at start-up (`Theme/ThemeFonts.cs`: a PrivateFontCollection
for GDI+ and AddFontResourceEx with FR_PRIVATE for GDI), with Segoe UI and Consolas as fallbacks when a
file is missing or cannot be used.
