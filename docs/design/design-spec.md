# PrintVect visual design (owner's spec, received 2026-10-06)

Source mock-ups: "Icon 6, LAN printer refined" and "Option F, Graphite dashboard" (design-tool exports,
HTML with inline styles; the values below are the ones to replicate). Icon source: `printvect-icon.svg`
(64-unit grid, tile `#1F3B73`, body white, tray/cable `#A9C4F5`, three PCs `#34D399`).

## Fonts
- Text: Manrope (bundled with the app, SIL Open Font License), fallback Segoe UI.
- PC names and addresses: JetBrains Mono (bundled, SIL Open Font License), fallback Consolas.
- Base text 14 px, weight 400 to 600. Card titles 16 px bold. Printer name 24 px bold; big numbers
  28 px bold. Small labels 11 px bold, uppercase, letter spacing 0.1em. Secondary text 13 px.

## Colours
- Background `#111418`; top bar `#151920`; cards `#181C22`.
- Borders `#262D37`; button outlines `#3A4350`.
- Text `#EEF1F5`; muted text `#9BA6B4`.
- Accent green `#3FCF8E` with text `#06150E` on it.
- Warning yellow `#F0B03F` with text `#1A1204` on it.
- Connected pill: background `#17352A`, text `#6FE0AE`. Offline pill: background `#232931`, text `#AEB8C4`.
- Switch knob `#FFFFFF`.

## Shape
- Cards: 12 px corners, 1 px border. Buttons: 8 px corners, at least 44 px tall.
- Status pills fully rounded. Active tab: 3 px accent-green underline.
- Mock-up size 1040 x 680 at 100 %; top bar 56 px; content padding 20/24 px; card gap 16 px.

## Decisions (owner delegated them on 2026-10-06; all of the below is agreed)
- Fonts: Manrope and JetBrains Mono are bundled (SIL Open Font License, licence text shipped beside
  them), loaded privately at start-up, fallbacks Segoe UI and Consolas.
- Order: M5 (installer with the new icon and the fonts) first, then M6 = this look plus hardening
  (copies prompt, presence signal, Pause, PIN screen, plain-language polish).
- Window frame: Windows' own dark title bar, no hand-drawn frame.
- The tray icon uses a simplified 16 px glyph (printer body and one green bar).

## Mapping to PrintVect (agreed)
- Tabs Office / Queue / Printers / Settings = Share my printers (this PC as host) / jobs sent and
  received / Use shared printers (printers from other PCs) / Settings with a Diagnostics section.
- "Computers" card and list = PCs that use this host, from a light presence signal (M6), with their
  Windows version; no "Send driver" (PrintVect needs no drivers).
- "Address for other computers" = "Other PCs find this PC as <name> (<IP>)" with Copy.
- "Add a computer" on the host = "How to connect a PC" (instructions); adding is done on the client.
- Printer card = the shared printers with per-printer ticks and the master "Sharing with the office" switch.
- "Pause queue" = stop handing new jobs to that printer (M6).
- Window frame: Windows' own dark title bar (Windows 10 1809+ / 11) rather than a hand-drawn one.
