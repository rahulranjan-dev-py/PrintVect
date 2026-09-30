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
| 11 | Press Win+R and type exactly `C:\PrintVect\PrintVect.App.exe /tray` (the folder must be part of the command), then Enter. Or open a Command Prompt and run `cd C:\PrintVect` followed by `PrintVect.App.exe /tray`. | Only the tray icon appears, no window. Left-click opens it. Then Exit. |
| 12 | Open a Command Prompt **as administrator**, run: `C:\PrintVect\PrintVect.Elevate.exe ping` then `echo %errorlevel%`. | It prints "PrintVect.Elevate is working and running as administrator on Windows ...", and `%errorlevel%` is 0. `C:\ProgramData\PrintVect\spool\elevate-result.json` and `logs\PrintVect-Elevate-<date>.log` exist. |
| 13 | In the same prompt run `C:\PrintVect\PrintVect.Elevate.exe nonsense` then `echo %errorlevel%`. | "Unknown command", errorlevel 2. |
| 14 | Optional, Windows 7 SP1 PC with .NET 4.8 installed: repeat steps 2, 4, 5, 10. | Same results. The Windows line should say "Windows 7 ... Service Pack 1 (build 7601)". |

Result on 2026-09-30, Windows 11 Pro 25H2: steps 1 to 10 and 12 to 13 as expected. Step 11 failed only
because the earlier text omitted the folder in the Win+R command; the step above is corrected.

What to paste back after M0:

1. The Diagnostics text from step 5 (one per Windows version tested).
2. Anything that did not match the "Expect this" column, with the step number.
3. If the program did not start at all: the newest file in `C:\ProgramData\PrintVect\logs\`, or
   the exact text of any error box.

## M1: host role (share a printer, receive and print jobs)

Host PC = the Windows 11 PC from M0 (it has the HP Laser printers and Microsoft Print to PDF).
`pvct-send.exe` is the test sender; it lives in the same folder as PrintVect.App.exe.
The test file `PrintVect-test-page.xps` is in that folder too (also in `docs/samples/`).

| # | Do this | Expect this |
|---|---------|-------------|
| 1 | Download the latest `PrintVect-build-<n>` artifact, unzip it over `C:\PrintVect` (replace all files). Start `PrintVect.App.exe`. | The window opens as before. The **Share my printers** tab now shows a table of the printers on this PC with columns Share, Printer, Friendly name, Status. |
| 2 | Tick **Share** for `Microsoft Print to PDF` and for the HP Laser you want to test. Double-click the Friendly name cell of each, type a short name, press Enter. **Write the two names down: every `pvct-send` command below must use them exactly** (the examples use `PDF` and `Mail Branch`). | The names stay after pressing Refresh. `C:\ProgramData\PrintVect\config.json` now lists them under `SharedPrinters`. |
| 3 | Tick **Share the ticked printers with the office**. | The line under it reads "Sharing is ON. Other PCs can send print jobs to this PC on port 9151." The status bar says "Sharing is ON". If Windows shows a "Windows Security Alert" for PrintVect.App, tick Private networks and click **Allow access**. |
| 4 | Open a Command Prompt (no admin needed): `cd C:\PrintVect` then `pvct-send list 127.0.0.1` | It prints "Host DESKTOP-... shares 2 printer(s)" and the two friendly names with their status. These are the names to use in the next steps. |
| 5 | `pvct-send PrintVect-test-page.xps 127.0.0.1 "PDF"` (use your name for the PDF printer from step 2) | A **Save Print Output As** window appears (from Microsoft Print to PDF). Save it as `C:\PrintVect\test.pdf` **within a minute**; PrintVect waits for Windows, and Windows waits for you. The command prints "printed - Printed on ..." (if you took longer than a minute it first says "printing", then "printed" a moment after you save). Two balloons appear from the tray: Printing... and Printed... The Share tab lists the job with state Printed and "Jobs printed today: 1". Open test.pdf: the PrintVect test page with a blue frame and five grey boxes. |
| 6 | `pvct-send PrintVect-test-page.xps 127.0.0.1 "Mail Branch"` (your name for the laser from step 2) | Paper comes out of the HP Laser within about 10 s and the command says printed. |
| 6a | **Only if nothing came out in step 6:** on the host open Settings, Bluetooth & devices, Printers & scanners, the HP Laser, **Open print queue**. Also print any page from Notepad straight to the HP Laser. | Report what the queue window shows for the job named "PrintVect: PrintVect-test-page from ..." (Printing, Error, Sent to printer, or nothing), whether the Notepad page printed, and whether the printer is connected by USB or Wi-Fi (Printer properties, Ports tab). |
| 7 | `pvct-send PrintVect-test-page.xps 127.0.0.1 "No such printer"` | "The host refused: No shared printer called ... Shared printers: ..." and errorlevel 1. |
| 8 | Untick **Share the ticked printers with the office**, then run `pvct-send list 127.0.0.1` | "127.0.0.1 is reachable but nothing is listening on port 9151..." and errorlevel 3. **Now tick sharing ON again and check the status bar says "Sharing is ON"; steps 9 to 11 need it.** |
| 9 | Second PC (Windows 7, 10 or 11 with .NET 4.8; a laptop on the office network is fine): copy the whole `C:\PrintVect` folder there. On the host, open **Diagnostics** and note the IPv4 address under Network (for you: `10.169.183.66` or `10.148.93.218`, whichever network the second PC is on). On the second PC: `pvct-send list <host-ip>`. | The same printer list as in step 4. If it says the host is not reachable, add the firewall rule on the host (command below) and try again. |
| 10 | On the second PC: `pvct-send PrintVect-test-page.xps <host-ip> "Mail Branch"` (your laser's name) | Paper comes out of the host's printer; balloons appear on the host; the job is listed in the host's Share tab with "From" = the second PC's name. |
| 11 | With sharing ON, right-click the host's tray icon, Exit, then start PrintVect again. | Sharing is ON again without touching anything (the status bar says so), because config.json remembers the last setting. (If it was OFF when you exited, it stays OFF; that is by design.) |
| 12 | Optional PIN test: on the host, Exit PrintVect, open `C:\ProgramData\PrintVect\config.json` in Notepad, change `"Pin": ""` to `"Pin": "1234"`, save, start PrintVect. From the second PC: send without `/pin`, then with `/pin 1234`. | The first is refused with a message about the PIN; the second prints. Set the PIN back to `""` afterwards. |
| 13 | Diagnostics tab, Copy to clipboard. | The new section "Printer sharing (host role)" shows sharing ON, the shared printers with ids, today's counts and the last jobs. |

Firewall rule for the host (only needed if step 9 cannot reach the host; run in a Command
Prompt **as administrator**, adjust the path if PrintVect is elsewhere):

```
netsh advfirewall firewall add rule name="PrintVect Jobs (TCP-In)" dir=in action=allow protocol=TCP localport=9151 program="C:\PrintVect\PrintVect.App.exe" profile=private,domain
```

Afterwards the Diagnostics tab shows "PrintVect Jobs (TCP-In) for TCP 9151: present". The
installer (M5) will add this rule itself.

Second run on 2026-09-30: the PDF printer worked (test.pdf shows the test page) but PrintVect reported
it as removed from the queue: Windows' AddJob only returns after the Save dialog is answered, and by
then the job has already left the queue. Fixed. The HP Laser job never came back from Windows and,
with a single print thread, every later job queued behind it ("2 job(s) ahead of it"). Now each printer
has its own print thread and a stuck job is reported after 10 minutes; step 6a was added to find out
why the HP Laser did not take the job.

Third check on 2026-09-30 (part A): the HP Laser prints from Notepad, is on USB001, and both queues
were empty, so the earlier hang happened inside .NET's printing library before any spooler job
existed. Build 8 prints .xps files through the Windows XPS Print API instead (the job shows in the
Windows print queue immediately, with page progress in the Share tab).

First run on 2026-09-30: steps 1 to 4, 7 and 8 passed. Steps 5, 6, 9 and 10 did not print only because
the commands used the example names instead of the names set in step 2, and because sharing had been left
OFF after step 8; the wording above was tightened accordingly.

Known limits at M1, by design: copies and duplex chosen on the sending side are not applied
(one copy prints); a Windows 7 host cannot print `.oxps` files; there is no Settings screen for
the PIN yet; the "Recent jobs" list and today's count are cleared when PrintVect exits (job
history that survives a restart comes with M3), and a job Windows had not finished at exit is not
re-printed after the restart (its file stays in `spool\incoming` for a day).

What to paste back after M1:

1. The Diagnostics text from step 13 (host).
2. The Command Prompt output of steps 4 to 8 and, if you had a second PC, 9 and 10.
3. If a job did not print: the host log `C:\ProgramData\PrintVect\logs\PrintVect-<date>.log`
   and, from the sending PC, `C:\ProgramData\PrintVect\logs\PrintVect-Send-<date>.log`. Both
   contain the job id, so the two sides can be matched.

## M2 and later

Steps are added here when each milestone is delivered.
