# Manual test steps

PrintVect cannot be tested where it is written (no Windows, no printer). The owner runs each
milestone on real PCs and pastes the results back. Each milestone below lists exactly what to
do, what should happen, and what to paste back if it does not.

Where to find things while testing:

- Logs: `C:\ProgramData\PrintVect\logs\PrintVect-<date>.log` (ProgramData is hidden; type the
  path into the Explorer address bar).
- Settings: `C:\ProgramData\PrintVect\config.json`.
- The Diagnostics tab's **Copy to clipboard** text contains everything a log-reading person
  needs; pasting that is usually enough.

## M0: scaffold, CI, empty tray app

Test on one Windows 10 or 11 PC first. Windows 7 is optional at this milestone.

| # | Do this | Expect this |
|---|---------|-------------|
| 1 | Download the latest `PrintVect-build-<n>` artifact from GitHub Actions and unzip it to `C:\PrintVect`. | The folder contains `PrintVect.App.exe`, `PrintVect.Elevate.exe`, `PrintVect.Core.dll`, `Newtonsoft.Json.dll`. |
| 2 | Double-click `PrintVect.App.exe`. (SmartScreen: More info, Run anyway.) | A window titled **PrintVect** opens with four tabs: Share my printers, Use shared printers, Settings, Diagnostics. A PrintVect icon appears in the tray near the clock (check the hidden icons arrow). The status bar reads "Sharing is OFF" and "Version 0.1.0". |
| 3 | Open the **Settings** tab. | It shows the settings file path `C:\ProgramData\PrintVect\config.json`, ports 9150 / 9151, "Shared PIN: not set". |
| 4 | Open the **Diagnostics** tab, wait a moment. | Text appears with sections Program, Windows, Network, Settings, Folders, Firewall rules, Printers on this PC, Log files, Last 200 log lines. The Windows line names your edition correctly (Windows 11 shows as Windows 11). Firewall rules say MISSING (expected before M5). Your printers are listed. |
| 5 | Press **Copy to clipboard**, open Notepad, paste. | The same text appears in Notepad. Status bar says "Copied". **Paste this text back to Claude.** |
| 6 | Press **Open log folder**. | Explorer opens `C:\ProgramData\PrintVect\logs` with today's `PrintVect-<date>.log`. |
| 7 | Close the window with the X. | The window disappears, the tray icon stays, and a balloon says PrintVect is still running (first time only). |
| 8 | Left-click the tray icon. | The window comes back. Right-click shows a menu with **Open PrintVect** and **Exit**. |
| 9 | With the window open, start `PrintVect.App.exe` a second time. | No second icon or window; the existing window comes to the front. |
| 10 | Right-click the tray icon, **Exit**. | Icon and window disappear. Task Manager shows no PrintVect.App.exe. |
| 11 | Start with `PrintVect.App.exe /tray` (Win+R, or a shortcut with the switch). | Only the tray icon appears, no window. Left-click opens it. Then Exit. |
| 12 | Open a Command Prompt **as administrator**, run: `C:\PrintVect\PrintVect.Elevate.exe ping` then `echo %errorlevel%`. | It prints "PrintVect.Elevate is working and running as administrator on Windows ...", and `%errorlevel%` is 0. `C:\ProgramData\PrintVect\spool\elevate-result.json` and `logs\PrintVect-Elevate-<date>.log` exist. |
| 13 | In the same prompt run `C:\PrintVect\PrintVect.Elevate.exe nonsense` then `echo %errorlevel%`. | "Unknown command", errorlevel 2. |
| 14 | Optional, Windows 7 SP1 PC with .NET 4.8 installed: repeat steps 2, 4, 5, 10. | Same results. The Windows line should say "Windows 7 ... Service Pack 1 (build 7601)". |

What to paste back after M0:

1. The Diagnostics text from step 5 (one per Windows version tested).
2. Anything that did not match the "Expect this" column, with the step number.
3. If the program did not start at all: the newest file in `C:\ProgramData\PrintVect\logs\`, or
   the exact text of any error box.

## M1 and later

Steps are added here when each milestone is delivered.
