# Bundled fonts

Both families are licensed under the SIL Open Font License 1.1; the licence text sits beside each
family and ships with the program (installer and build artifact). The files are unmodified copies.

| Folder | Files | Source |
|--------|-------|--------|
| `Manrope/` | `Manrope-VariableFont_wght.ttf` (weights 200 to 800) | github.com/google/fonts, `ofl/manrope` |
| `JetBrainsMono/` | `JetBrainsMono-Regular.ttf`, `-Medium.ttf`, `-Bold.ttf` | github.com/JetBrains/JetBrainsMono release 2.304 |

The app loads them privately at start-up (no system-wide install) from M6 on, with Segoe UI and
Consolas as fallbacks when a file is missing.
