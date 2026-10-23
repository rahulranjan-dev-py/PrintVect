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
| 1 | Download the latest `PrintVect-build-<n>` artifact (if Chrome blocks it as dangerous, use **Download dangerous file** from the entry's three-dot menu in the downloads list; see README), unzip it over `C:\PrintVect` (replace all files). Start `PrintVect.App.exe`. | The window opens as before. The **Share my printers** tab now shows a table of the printers on this PC with columns Share, Printer, Friendly name, Status. |
| 2 | Tick **Share** for `Microsoft Print to PDF` and for the HP Laser you want to test. Double-click the Friendly name cell of each, type a short name, press Enter. **Write the two names down: every `pvct-send` command below must use them exactly** (the examples use `PDF` and `Mail Branch`). | The names stay after pressing Refresh. `C:\ProgramData\PrintVect\config.json` now lists them under `SharedPrinters`. |
| 3 | Tick **Share the ticked printers with the office**. | The line under it reads "Sharing is ON. Other PCs can send print jobs to this PC on port 9151." The status bar says "Sharing is ON". If Windows shows a "Windows Security Alert" for PrintVect.App, tick Private networks and click **Allow access**. |
| 4 | Open a Command Prompt (no admin needed): `cd C:\PrintVect` then `pvct-send list 127.0.0.1` | It prints "Host DESKTOP-... shares 2 printer(s)" and the two friendly names with their status. These are the names to use in the next steps. |
| 5 | `pvct-send PrintVect-test-page.xps 127.0.0.1 "PDF"` (use your name for the PDF printer from step 2) | A **Save Print Output As** window appears (from Microsoft Print to PDF). Save it as `C:\PrintVect\test.pdf` **within a minute**; PrintVect waits for Windows, and Windows waits for you. The command prints "printed - Printed on ..." (if you took longer than a minute it first says "printing", then "printed" a moment after you save). Two balloons appear from the tray: Printing... and Printed... The Share tab lists the job with state Printed and "Jobs printed today: 1". Open test.pdf: the PrintVect test page with a blue frame and five grey boxes. |
| 6 | `pvct-send PrintVect-test-page.xps 127.0.0.1 "Mail Branch"` (your name for the laser from step 2) | Paper comes out of the HP Laser within about 10 s and the command says printed. |
| 6b | **Only if step 6 failed:** `pvct-send PrintVect-test-shapes.xps 127.0.0.1 "Mail Branch"` (same page without any text or font). | If this prints a blue cross and a grey diamond, the font in the text page is the problem. |
| 6c | **Only if step 6 failed:** make a Windows-made XPS: Settings, Apps, Optional features, More Windows features, tick **Microsoft XPS Document Writer**, OK. Print any page from Notepad to "Microsoft XPS Document Writer", choose Save as type **XPS Document (*.xps)**, save as `C:\PrintVect\notepad.xps`. Then `pvct-send notepad.xps 127.0.0.1 "Mail Branch"`. | If this prints, PrintVect's own test page is the problem; if it fails too, the printing path is. Report the host log lines "Driver ...", "Spooler job N opened ... data type ..." and "Windows job N status: ...". |
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

Build 9 failed on both printers with "Unable to cast COM object ... IXpsPrintJob ... E_NOINTERFACE":
the job object refused the .NET interface check on both thread kinds (build 12 tried MTA) although
Windows had started the job (the Save window appeared). Build 13 calls the job and stream through
their COM function tables without that check and logs a probe of the interface ids. The PDF test
also left an empty test.pdf; delete it before retrying.

Build 16 on 2026-10-01: **M1 accepted.** Steps 5, 6, 9, 10, 11 and 13 passed: the PDF printer showed the
Save window again (System.Printing, port PORTPROMPT:), the HP Laser printed through the direct spooler path
(port USB001, XPS2GDI), the Windows 10 PC DESKTOP-CTQLFOK printed on the host's laser over the office LAN,
and sharing was ON again after Exit and restart.

Build 15 on 2026-10-01: paper came out of the HP Laser for the first time (the direct spooler path with
the XPS2GDI data type). Microsoft Print to PDF, on the same direct path (XPS_PASS), did not show the Save
window: Windows saved the file to the Documents folder on its own and the PDF does not open. Build 16
sends printers whose port asks for a file name (PORTPROMPT: or FILE:, that is Microsoft Print to PDF and
the XPS Document Writer) through System.Printing again, the path that showed the Save window and made a
readable test.pdf in the second run; paper printers keep the direct path. The host log now names each
printer's port ("is on port USB001"). Delete the stray PDFs from Documents before retrying step 5.

Build 14 on 2026-10-01: with the ticket stream closed, the XPS Print API failed both jobs within a
second with 0x80040003 (OLE_E_ADVISENOTSUPPORTED), for the HP before any spooler job existed. That is
Windows' deprecated XPS Print API failing on its own. Build 15 hands the file to the spooler with the
plain winspool calls instead (XPS_PASS or XPS2GDI data type by driver type) and logs the Windows job
status; steps 6b and 6c below tell a document problem from a printer problem.

Build 13 on 2026-10-01: the job opened and the document reached the spooler, but nothing printed
and the PDF stayed empty, because the empty print ticket stream was never closed and Windows waits
for it. Build 14 closes it. The Share tab now also shows a job's progress while it prints.

Third check on 2026-09-30 (part A): the HP Laser prints from Notepad, is on USB001, and both queues
were empty, so the earlier hang happened inside .NET's printing library before any spooler job
existed. Build 9 prints .xps files through the Windows XPS Print API instead (the job shows in the
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

## M2: client role (print from another PC through a virtual printer)

Host PC = the Windows 11 PC from M1 (DESKTOP-JK66OQV, sharing ON, "Mail Branch" = the HP Laser).
Client PC = the Windows 10 PC (DESKTOP-CTQLFOK). Both run the same build. The client needs no
driver for the laser: PrintVect creates a printer that uses Windows' own XPS Document Writer and
ships the file to the host.

| # | Do this | Expect this |
|---|---------|-------------|
| 1 | On **both** PCs: Exit PrintVect from the tray, download the latest `PrintVect-build-<n>`, unzip it over `C:\PrintVect` (replace all), start `PrintVect.App.exe`. On the host check the status bar says "Sharing is ON". | Both windows open. On the client, the **Use shared printers** tab shows a "PC name or IP address" box, **Look up**, an empty printer list, **Add to this PC**, and the lists "Printers from other PCs on this PC" and "Jobs sent from this PC". |
| 2 | On the client: Use tab, type the host's address `10.148.93.218` (its PC name `DESKTOP-JK66OQV` works too) and press **Look up**. | "DESKTOP-JK66OQV shares 2 printer(s):" and the list shows Mail Branch and Microsoft Print to PDF with their status. |
| 3 | Select **Mail Branch** and press **Add to this PC**. Windows shows its permission window (UAC): choose **Yes** (if it asks for an administrator name and password, type them). | After a few seconds: "PrintVect - Mail Branch @DESKTOP-JK66OQV is ready. Print to it from any program; the pages come out on DESKTOP-JK66OQV." The printer is listed under "Printers from other PCs on this PC", and also in Windows Settings, Printers & scanners. |
| 3a | **Only if step 3 failed:** paste the message shown and, from the client, both logs `C:\ProgramData\PrintVect\logs\PrintVect-<date>.log` and `PrintVect-Elevate-<date>.log`. The Elevate log lists the installed printer drivers; the printer needs "Microsoft XPS Document Writer" (any version). | |
| 4 | Select the new printer in the list and press **Send test page**. | Within about 10 s the job appears under "Jobs sent from this PC" as Sending, then Printed; the PrintVect test page comes out of the host's laser; a balloon says "PrintVect-test-page printed on DESKTOP-JK66OQV". On the host, the Share tab lists the job from DESKTOP-CTQLFOK. |
| 5 | **The real test.** On the client open Notepad, type a line, File, Print, choose "PrintVect - Mail Branch @DESKTOP-JK66OQV", Print. | Within 10 s the page comes out of the host's laser. The job list shows the document name (e.g. "Untitled - Notepad") with state Printed and a balloon appears. |
| 6 | Print the same Notepad page twice quickly (Print, Print again straight away). | Two pages come out and two jobs are listed. |
| 7 | Host-off test: on the host untick "Share the ticked printers with the office". On the client print from Notepad again. | Within about a minute (three tries, each to the IP and then to the PC name) the job turns **Waiting** (orange) with "DESKTOP-JK66OQV could not be reached after 3 tries ... press Retry", a balloon says the same, and "Waiting jobs" shows 1 next to the printer. Turn sharing ON again on the host, then on the client select the printer and press **Retry waiting jobs**: the page prints **as the same job**: the orange row turns Sending, then Printed. |
| 8 | Exit PrintVect on the client (tray, Exit), start it again, print from Notepad. | It still prints: the printer and its folder are remembered in config.json. |
| 9 | Optional: add "Microsoft Print to PDF" the same way and print to it from the client. | The **Save Print Output As** window appears on the **host** PC (that is where the PDF printer is); after saving there the job shows Printed on the client. |
| 10 | Client: Diagnostics tab, Copy to clipboard. | The section "Printers from other PCs (client role)" lists the printer with its folder, "Elevate helper: present", and the last jobs. |
| 11 | Client: select the printer, press **Remove**, Yes, then Yes in the Windows permission window. | The printer disappears from the list and from Windows Settings, and `C:\ProgramData\PrintVect\spool\<id>` is gone. Add it again afterwards if you want to keep using it. |

Build 20 on 2026-10-05: steps 1 to 6, 8 and 10 passed on the Windows 10 PC DESKTOP-CTQLFOK. The
printer was created with the "Microsoft XPS Document Writer v4" driver (the older writer is not installed
there), so Notepad jobs arrive as OpenXPS; the host printed them on the HP Laser through the spooler
(XPS2GDI), which settles pitfall 11.2 for Windows 10/11 hosts. Step 7: the job went Waiting as expected,
but Retry created a new job instead of resuming the waiting one. Build 21 resends the waiting file under
its own job id, so the orange row itself turns Sending and Printed, and it also names jobs after the
document the user printed ("Untitled - Notepad", read from the virtual printer's own queue while the
file is written) instead of job-<id>. Step 11 (Remove) is still to be reported.

Build 22 on 2026-10-05, on a third PC (host NIRSACHATTISO sharing "SPM"): Add to this PC failed with
"Neither Microsoft XPS Document Writer nor ... v4 is installed on this PC". That PC has no XPS Document
Writer at all (the Windows feature is off). Build 23 installs Windows' own XPS writer driver from the
driver store and, when the store does not have it, turns on the Windows feature "Microsoft XPS Document
Writer" itself (DISM, no internet needed, up to a few minutes) before creating the printer. The Elevate
log shows each step; if Windows asks for a restart, the message says so.

Build 23 on 2026-10-05: **M2 accepted.** On the third PC the SPM printer of NIRSACHATTISO was added (the
XPS writer driver installed by PrintVect) and the test page printed; on the Windows 10 PC step 7 (Retry
resumes the same job) and step 11 (Remove) passed.

What happens underneath (for reading the logs): the client's PrintVect.Elevate.exe creates the folder
`spool\<printer id>`, gives Users modify rights on it, adds a Local Port whose name is that folder's
`job.xps`, and creates the printer with the Windows XPS Document Writer driver (the older
"Microsoft XPS Document Writer" when installed, else "... v4"). Each print writes `job.xps`; the watcher
renames it to `job-<guid>.xps` (or `.oxps`, PrintVect looks inside the file to tell), sends it, and keeps it
in `sent\` for an hour. The host prints `.oxps` through the spooler on Windows 8 or later.

Known limits at M2, by design: the number of copies and duplex chosen in the print window are not
applied (one copy prints, as the brief says); the paper size is whatever the XPS writer uses by default
(A4 on an Indian Windows); a PDF printer's Save window appears on the host; hosts are typed by name
or IP (automatic discovery is M3); the job list is cleared when PrintVect exits (M3).

What to paste back after M2:

1. For each step, whether it matched the Expect column.
2. From the client: the Diagnostics text (step 10), `PrintVect-<date>.log` and `PrintVect-Elevate-<date>.log`.
3. From the host: `PrintVect-<date>.log` (the job ids match the client's log).

## M3: finding hosts by themselves, job history after a restart

Host PC = the Windows 11 PC (sharing ON). Client PC = the Windows 10 PC (its "Mail Branch" printer
from M2 is still there) or any other office PC with PrintVect. Nobody should have to type an address
any more: while the **Use shared printers** tab is open, PrintVect asks the office every 10 seconds
(UDP port 9150, out of every network card) and lists every PC that answers.

| # | Do this | Expect this |
|---|---------|-------------|
| 1 | On both PCs: Exit PrintVect, unzip the latest `PrintVect-build-<n>` over `C:\PrintVect`, start it. On the host check "Sharing is ON". If Windows shows a "Windows Security Alert" for PrintVect.App on the host, tick Private networks and **Allow access** (that is the UDP port). | Both windows open. |
| 2 | Host: Diagnostics tab, Refresh. | Under "Printer sharing (host role)": "Discovery: answering on UDP port 9150 (0 request(s) answered)". If it says OFF with a reason, paste it. |
| 3 | Client: open the **Use shared printers** tab and wait up to 10 s. | "Printers found in the office:" lists Mail Branch and Microsoft Print to PDF with Host PC DESKTOP-JK66OQV, **without typing anything**. The line next to the heading says "2 printer(s) on 1 PC(s)". A printer already on this PC is grey with ", on this PC" after its status. |
| 3a | **Only if the list stays empty for 30 s:** on the host run the UDP firewall command below in a Command Prompt **as administrator**, then on the client click another tab and come back to the Use tab. | The list fills within 10 s. If not, paste the Diagnostics text of both PCs. |
| 4 | Client: select a printer that is not on this PC yet (Microsoft Print to PDF), **Add to this PC**, Yes to Windows. | Added as in M2; it now shows ", on this PC" in the found list and appears under "Printers from other PCs on this PC". |
| 5 | Host: untick sharing. Client: watch the found list. | Within about 30 s the host's printers disappear and the grey line says "No PC is sharing a printer right now...". Tick sharing ON again on the host: they are back within 10 s. |
| 6 | Restart test: on the client, Exit PrintVect and start it again; look at "Jobs sent from this PC". Do the same on the host and look at the Share tab's job list. | The jobs from before the restart are still listed on both PCs, with their states, and "Printed today" still counts them (files `jobs-client.json` and `jobs-host.json` in `C:\ProgramData\PrintVect`). |
| 7 | Typing still works: in "Not listed? PC name or IP address" type `DESKTOP-JK66OQV`, **Look up**. | The status line says "DESKTOP-JK66OQV shares 2 printer(s)" and the host stays in the found list. |
| 8 | Client: Diagnostics, Refresh (with the Use tab visited first). | "Discovery: looking every 10 s on UDP port 9150" or "idle (runs while the Use tab is open)", and "Hosts found: DESKTOP-JK66OQV at 10.148.93.218:9151 with 2 printer(s), seen hh:mm:ss". |
| 9 | Optional, the third PC (SPM): open its Use tab. | It lists the host(s) on its network by itself too. |

Firewall rule for discovery on the host (only if step 3a was needed; run as administrator):

```
netsh advfirewall firewall add rule name="PrintVect Discovery (UDP-In)" dir=in action=allow protocol=UDP localport=9150 program="C:\PrintVect\PrintVect.App.exe" profile=private,domain
```

Why a client needs no rule: Windows lets the answers in because this PC asked first. Why the host
answers from the right card: it picks the address on the asker's subnet, so the phone tether on
"Ethernet 2" does not get in the way of the office LAN. Broadcasts do not cross subnets; a host on
another subnet is still reachable by typing its name or IP.

Build 26 on 2026-10-07: **M3 accepted.** Both PCs found each other by themselves (the Windows 11 host
answered 148 requests, NIRSACHATTISO 122), a 2 MB Word letter printed from the host onto NIRSACHATTISO's
laser, Remove and the restart worked. Two things seen in the diagnostics were tidied in build 27: a PC with
two network cards flipped between its own two addresses every round (a log line each time; the found list
now keeps the address that still answers), and every PC listed its own printers under "found in the
office" (now hidden there; Diagnostics still shows them).

Copies (seen 2026-10-06): printing 2 copies from Word to a PrintVect printer gave one page, because the
XPS writer records the copies count inside the file instead of repeating the pages. From build 26 the client
reads that count from the file's print ticket and the host prints the document that many times (each copy its
own Windows job); the job list shows "2 copies". Chrome hides the Copies field for the XPS writer altogether,
so Chrome users still print twice until M6 adds PrintVect's own copies prompt.

Known limits at M3, by design: the found list only shows hosts on this PC's own subnet(s); a host that
changes its IP address is followed automatically only while the Use tab is open (otherwise the next
job tries the IP, then the PC name); the Settings tab is still read-only (M6).

What to paste back after M3: whether each step matched, and the Diagnostics text of both PCs.

## M4: Windows 7 pass (skipped)

Decided 2026-10-06: the office has no Windows 7 PC, so the Windows 7 pass of the brief is skipped and
PrintVect is tested on Windows 10 and 11 only. The code keeps what Windows 7 compatibility costs nothing
(.NET Framework 4.8, no newer Windows APIs, the v3 XPS writer driver and System.Printing paths for a
Windows 7 PC), but none of it is verified. After M3 the next milestone is M5 (installer), then M6.

## M5: installer

One setup program for every PC, built by CI as the artifact `PrintVect-Setup-<n>` (inside it:
`PrintVect-Setup-<version>.exe`). It installs to `C:\Program Files\PrintVect`, keeps the settings that are
already in `C:\ProgramData\PrintVect`, opens the two firewall ports, and starts PrintVect with Windows.
The program is not signed yet, so Windows SmartScreen warns once: **More info**, then **Run anyway**.

| # | Do this | Expect this |
|---|---------|-------------|
| 1 | On the Windows 10 PC: download `PrintVect-Setup-<n>`, unzip it, run `PrintVect-Setup-0.1.0.exe` (More info, Run anyway if SmartScreen appears). Accept the licence, keep both tick boxes ticked, Install, Finish with "Start PrintVect now" ticked. | Setup closes the running PrintVect by itself, shows a note that the old copy in `C:\PrintVect` can be deleted, and PrintVect opens. The printer "PrintVect - Mail Branch @DESKTOP-JK66OQV" is still under "Printers from other PCs on this PC" (settings were kept). |
| 2 | Diagnostics tab, Refresh. | "Program file: C:\Program Files\PrintVect\PrintVect.App.exe"; under Firewall rules both "PrintVect Jobs (TCP-In)" and "PrintVect Discovery (UDP-In)" say **present**. |
| 3 | Print from Notepad to the PrintVect printer. | Prints on the host as before. |
| 4 | Delete the folder `C:\PrintVect`, then sign out of Windows and in again (or restart the PC). | PrintVect starts by itself in the tray (icon in the notification area, no window). The Start menu has a PrintVect entry. |
| 5 | On the host (Windows 11): run the same setup. | Sharing is still ON afterwards with Mail Branch ticked; Diagnostics shows both firewall rules present; the client prints to it. |
| 6 | Uninstall test on the third PC (NIRSACHATTISO): Settings, Apps, Installed apps, PrintVect, Uninstall, Yes. | Windows shows no PrintVect printer any more (Settings, Printers & scanners), the tray icon is gone, `C:\Program Files\PrintVect` and `C:\ProgramData\PrintVect` are gone, and the two firewall rules are gone (Diagnostics on another PC cannot show that; `netsh advfirewall firewall show rule name="PrintVect Jobs (TCP-In)"` in a Command Prompt says "No rules match"). Install again afterwards; the SPM printer must be added again, because an uninstall removes everything by design. |
| 7 | Second user: on any PC, sign in as a different Windows user and start PrintVect from the Start menu. | It starts and shows the same printers and settings (ProgramData is writable for every user now). |

Known limits at M5, by design: the setup program is unsigned (SmartScreen warning until a code-signing
certificate is bought); an uninstall deletes settings, history and logs with the program; there is no
Windows 7 prerequisites page (M4 skipped).

What to paste back after M5: whether each step matched, and the Diagnostics text after step 2.

Build 32 on 2026-10-09: **M5 accepted.** The setup program installed to `C:\Program Files\PrintVect` on the
Windows 10 PC, both firewall rules were present in Diagnostics, settings and the two remote printers were kept,
and the PC printed its real work through PrintVect all day (12 jobs to SPM on NIRSACHATTISO; the 4 jobs from
the minutes that PC was unreachable stayed under Retry, as designed).

## M6a: updates from inside the program

Asked for by the owner on 2026-10-09 ("auto update feature in software when new update is released").
This is the one place PrintVect uses the internet: once a day (45 s after it starts, then every 24 h while
it runs) it asks github.com for this repository's latest release. When that release is newer, a balloon and
a yellow strip in the window say so, and **Update now** downloads the setup program, checks its size and
SHA-256, and runs it silently. Windows asks its permission question once, PrintVect closes for a few
seconds, installs over itself and opens again. Printing never touches the internet, and the daily look can
be switched off in Settings (Check now still works).

PrintVect looks for new versions on the **Releases** page of the GitHub repository, so a release must exist
before any PC can update. Making one is a few clicks on the GitHub website (the build environment is not
allowed to publish on the repository, so this part is the owner's):

1. Open https://github.com/rahulranjan-dev-py/PrintVect/releases/new (signed in to GitHub).
2. Click **Choose a tag**, type `v0.2.0`, then click **Create new tag: v0.2.0 on publish**.
3. Click **Target: main** and pick the branch **ccr-5c5bb225-3h4f6u**. Do not skip this: the main branch
   has nothing to build, and the files would never appear.
4. In **Release title** type `PrintVect 0.2.0`. Leave everything else as it is.
5. Click **Publish release**.
6. Wait five minutes and refresh the page: the release now lists `PrintVect-Setup-0.2.0.exe` and
   `PrintVect-Setup-0.2.0.exe.sha256`. GitHub built and attached them (Actions, run named **release**).

The test needs two releases: v0.2.0 is installed by hand on each PC; v0.2.1, made the same way after the
version has been raised, is the one the PCs must find and install on their own.

| # | Do this | Expect this |
|---|---------|-------------|
| 0 | Make release v0.2.0 with the six clicks above. | After five minutes the release page lists `PrintVect-Setup-0.2.0.exe` (about 2.2 MB) and `PrintVect-Setup-0.2.0.exe.sha256`. |
| 1 | On the Windows 10 PC: open the repository's **Releases** page on GitHub, download `PrintVect-Setup-0.2.0.exe` from release v0.2.0 and run it (More info, Run anyway if SmartScreen appears; Yes to the permission question). | PrintVect opens again by itself after the install. The status bar says "Version 0.2.0". Settings, printers and the job history are as before. (No "was updated" balloon this time: version 0.1.0 did not yet note which version ran; from now on every first start of a new version shows one.) |
| 2 | Settings tab. | A new **Updates** group at the bottom: the tick box "Look for a newer PrintVect once a day" is ticked, the text says "Not checked yet in this run..." or, after about a minute, "PrintVect 0.2.0 is the latest version (checked HH:MM)". |
| 3 | Press **Check now**. | The text says "Asking github.com..." for a moment, then "PrintVect 0.2.0 is the latest version (checked HH:MM)". |
| 4 | Unplug the network cable (or turn Wi-Fi off), press **Check now**, plug it back in. | "Could not check for a newer version at HH:MM: this PC cannot reach github.com (is it connected to the internet?)". Printing and the rest of PrintVect carry on. |
| 5 | Diagnostics tab, Refresh. | A new section **Updates** with "This PC runs 0.2.0; daily check ON, next at ...", the last check result and "Release feed: https://api.github.com/repos/rahulranjan-dev-py/PrintVect/releases/latest". |
| 6 | Install v0.2.0 the same way on the host (Windows 11) and on NIRSACHATTISO, then tell me. I raise the version to 0.2.1 and push; you then make release v0.2.1 with the same six clicks (tag `v0.2.1`, title `PrintVect 0.2.1`). | |
| 7 | Within a day (or right away with **Check now**) on each PC. | Balloon "A newer PrintVect is ready: PrintVect 0.2.1 is ready to install..." and a yellow strip at the top of the window "A newer PrintVect (0.2.1) is ready to install" with **Update now** and **Later**. Settings says "PrintVect 0.2.1 is ready to install; this PC has 0.2.0...". |
| 8 | Press **Update now** (on the host, while nothing is printing). | "Downloading PrintVect 0.2.1: x of 2.x MB", then "Starting the update...". The Windows permission question appears for `PrintVect-Setup-0.2.1.exe`: Yes. A small setup progress window shows for a few seconds, PrintVect disappears from the tray and comes back with its window open; the status bar says "Version 0.2.1" and the balloon "This PC now runs PrintVect 0.2.1" appears. Sharing is still ON and the shared printers unchanged. |
| 9 | Press **Update now** on another PC, but answer **No** to the Windows permission question. | PrintVect keeps running: Settings says "The update did not run (the setup program ended with code N). Press Update now again and answer Yes...". Press it again and answer Yes: the update completes as in step 8. |
| 10 | Print from the Windows 10 PC to the host's printer, and from the host to its own printer. | Prints as before (an update keeps settings, printers and job history). |
| 11 | On one PC untick "Look for a newer PrintVect once a day", Exit PrintVect from the tray, start it again, Settings. | The box stays unticked and the text says "Automatic checks are off. Press Check now to look once." The log has "Update check: off in Settings". Tick it again afterwards. |

What to paste back after M6a: whether each step matched, the Diagnostics text of step 5, and, if step 8
or 9 went wrong, the newest `update-0.2.1.log` from `C:\ProgramData\PrintVect\updates\`.

Known limits at M6a, by design: the Windows permission question cannot be avoided without a Windows
Service (the setup program needs administrator rights), so an update is never installed without a click;
the setup program is still unsigned, which is why the permission window says "Unknown publisher"; a PC
without internet simply never sees updates (install the setup program by hand there).

Release v0.2.0 on 2026-10-17: **steps 0 to 6 passed** (release made on the website, setup files attached by
the release run, 0.2.0 installed by hand on the Windows 10 PC, the Windows 11 host and NIRSACHATTISO; Check now,
the offline message and the Diagnostics Updates section as expected). Version raised to 0.2.1 for steps 7 to 11.

Release v0.2.1 on 2026-10-17: **M6a accepted.** Steps 7 to 11 passed: every PC found 0.2.1 by itself, the host
updated with one click and came back as 0.2.1 with sharing still ON, a PC that answered No to the permission
question kept running and updated on the second try, printing worked in both directions afterwards, and the
daily check stayed off across a restart when unticked.

## M6b-1: the new look

The owner's Graphite design (docs/design/design-spec.md) on every page: dark window with Windows' own dark
title bar, Manrope for text and JetBrains Mono for PC names (both shipped with PrintVect), a top bar with the
pages and the sharing pill, cards, green switches and buttons. The pages are now **Share my printers**,
**Jobs** (new: the job lists that were at the bottom of the two sharing pages), **Use shared printers** and
**Settings** (Diagnostics is a card on it). Nothing about printing, sharing or finding printers changed.

It arrives through the updater from M6a, which is tested once more on the way: version 0.3.0.

| # | Do this | Expect this |
|---|---------|-------------|
| 0 | Make release v0.3.0 with the six clicks of M6a (tag `v0.3.0`, title `PrintVect 0.3.0`, target **main** once pull request #1 is merged, otherwise the branch **ccr-5c5bb225-3h4f6u**). | After five minutes the release lists `PrintVect-Setup-0.3.0.exe` and its `.sha256`. |
| 1 | On each PC: Settings, **Check now**, then **Update now** in the yellow strip (Yes to Windows). | PrintVect comes back dark. Status bar at the bottom right: "Version 0.3.0". The title bar is dark too (on Windows 11 the same colour as the bar below it). |
| 2 | Look at the top bar and click each page; then press Ctrl+Tab a few times. | Four pages in this order: Share my printers, Jobs, Use shared printers, Settings. The open one is white with a green line under it. On the right a pill: green **Sharing ON** on the host, grey **Sharing OFF** on a PC that shares nothing. Ctrl+Tab moves to the next page. |
| 3 | Host, Share my printers. | Card **THIS PC**: the PC name in large letters, "Other PCs find this PC as DESKTOP-... (10.148.93.x, ...)" and **Copy**. Card **SHARING WITH THE OFFICE**: the switch on (green). Card **Printers on this PC** with the tick boxes, the names for the office and the status. On the right **PRINTED TODAY** with a number, and **How to connect a PC**. |
| 4 | Press **Copy**, then paste into Notepad. | The status bar says "Copied: DESKTOP-... (...)", and Notepad gets the same text. |
| 5 | Turn the switch off, look at the pill, turn it on again. | Pill grey "Sharing OFF" and "Sharing is OFF. Other PCs cannot print here."; then green again. |
| 6 | Double-click the name of a shared printer, add " 2" to it, press Enter. On the Windows 10 PC open Use shared printers. | Within a few seconds the Windows 10 PC lists the printer with the new name. Change it back afterwards. |
| 7 | Windows 10 PC: Use shared printers. | Card **Printers in the office** lists the host's printers (the PC name in the narrow typewriter-like font); **Add to this PC** is green; the "Not listed?" box and **Look up** sit beside it. Card **Printers from other PCs on this PC** lists Mail Branch with **Send test page**, **Retry waiting jobs**, **Remove**. |
| 8 | There: select Mail Branch, **Send test page**. Open Jobs on both PCs. | The page prints. Windows 10 PC, **Sent from this PC**: a new row whose state turns green "Printed". Host, **Received from other PCs**: the same job, green "Printed"; the counts on the right of both titles and the host's PRINTED TODAY went up by one. |
| 9 | Print a normal document from Word or Notepad to the PrintVect printer. | Prints as before; it appears on the Jobs pages as in step 8. |
| 10 | Settings page. | Left: the settings, then **Updates** with its green switch and "PrintVect 0.3.0 is the latest version". Right: **Diagnostics** fills in by itself; **Copy to clipboard** (green) puts it on the clipboard and the status bar says so; **Open log folder** opens Explorer. In the text, the log lines include "Fonts: 5 of 5 font files loaded; text Manrope, names JetBrains Mono". |
| 11 | Right-click the tray icon. | The menu is dark too (Open PrintVect, Exit). |
| 12 | Make the window as small as it goes, then maximise it. | Nothing overlaps or is cut off in the middle of a word at the smallest size (long help texts may end early); maximised, the cards grow with the window. |
| 13 | If any PC uses a display scale above 100 % (Settings, System, Display, Scale): look at the pages there. | Text is sharp and the same layout, just larger. |

What to paste back after M6b-1: whether each step matched, **one screenshot of each page** from the host
(Windows key + Shift + S, then paste into the chat), and anything that looks wrong or hard to read.

Known limits of M6b-1, by design: the Windows permission window and PrintVect's question boxes ("Remove ...?")
stay light, because Windows draws them; on Windows 10 before version 1809 the title bar stays light; the small
capitals labels have no extra letter spacing.

Release v0.3.0 on 2026-10-17 (published by Run workflow from main after the release workflow fix): the owner
reported **all steps matched**; no screenshots or Diagnostics text came back, so the Windows-only parts (dark title
bar, dark scroll bars, scaling) are recorded as reported, not seen.

## M6b-2: hardening

What is new, all in version 0.4.0:

- **PCs that use this PC** (Share my printers, right): every PC that added one of this PC's printers says hello every
  5 minutes, with its Windows and PrintVect version; green "Connected", grey "Last seen HH:MM" after 12 minutes.
- **Copies question**: printing from a program to a PrintVect printer first asks how many copies (− / +, the arrow
  keys or typing a number; Enter prints, Esc cancels). Nobody answers: after 60 seconds it prints the number shown.
  Send test page and Retry never ask. Settings can turn it off ("Don't ask again" in the window does the same).
- **Pause printer / Resume printer** (Share my printers, for the selected shared printer): jobs from other PCs are
  accepted and wait; Resume prints them in order. The pause survives a restart; the other PCs see "Paused".
- **Office PIN** (Settings, Set PIN…): the same PIN on every PC. A job refused for the PIN, a job the host lost
  (PrintVect closed there) and a job whose host stopped answering after receiving it all go to **Retry waiting
  jobs** on the sender, with a message that says what happened.
- **Settings** can now be changed: office PIN, Start with Windows (Windows asks for permission once), the copies
  question, how long sent files are kept. Ports stay in the settings file (every PC must use the same ones).
- Error messages name PCs, not IP addresses ("COUNTER1 is not reachable on the network...").

Every PC needs 0.4.0: a host still on 0.3.0 logs an "unknown request type" line for each hello, and shows no PCs.

| # | Do this | Expect this |
|---|---------|-------------|
| 0 | Tell me to publish v0.4.0 (or Actions, release, Run workflow on main). Then on each PC: Settings, Check now, Update now. | Status bar: "Version 0.4.0" on every PC. |
| 1 | Host, Share my printers, card **PCs that use this PC**, about a minute after the others updated. | The Windows 10 PC and NIRSACHATTISO are listed by name, with "Windows 10 ... · PrintVect 0.4.0" under the name and a green **Connected** pill. |
| 2 | Exit PrintVect on the Windows 10 PC (tray icon, Exit). Wait 15 minutes, look at the host's card again. Start PrintVect there again. | Its pill turns grey "Last seen HH:MM". After the restart it is green again within a minute. |
| 3 | Windows 10 PC: print one page from Notepad to "PrintVect - Mail Branch @..." | A window "how many copies?" appears at once: the document name, the printer and host, a big 1, − and +. Press + once, then **Print**. Two pages come out on the host. |
| 4 | Print again and press **Cancel**. | Nothing prints. Jobs page, Sent from this PC: the job is grey "Cancelled". |
| 5 | Print again and do not touch the window. | The Print button counts down from 60; at 0 the window closes and one page prints. |
| 6 | Use shared printers, Mail Branch, **Send test page**. | No copies window; the test page prints. |
| 7 | Host: Share my printers, click the row of the shared printer (Mail Branch), **Pause printer**. On the Windows 10 PC open Use shared printers. | Host: its status turns yellow "Paused", the button now says **Resume printer** (green). Windows 10 PC: the printer's status says "Paused". |
| 8 | Windows 10 PC: print one page (1 copy). | Nothing prints. Both Jobs pages: "Waiting on <host>: Mail Branch is paused there. It prints when someone on <host> presses Resume." |
| 9 | Host: **Resume printer**. | The page prints within a few seconds; both Jobs pages turn green "Printed". |
| 10 | Host: Settings, **Set PIN…**, type 1234 twice, **Save PIN**. Windows 10 PC: print one page. | Host: "Office PIN saved...". The page does not print; the Windows 10 PC's job is yellow "Waiting": "<host> asks for the office PIN, and this job came with a different one or none... Press Retry waiting jobs to send it again." |
| 11 | Windows 10 PC: Settings, Set PIN…, 1234 twice, Save. Use shared printers, Mail Branch, **Retry waiting jobs**. | The page prints. Afterwards remove the PIN on both PCs (Settings, Remove PIN, Yes), or set it on every PC. |
| 12 | The brief's M6 test, host off mid-job: host Pause printer; Windows 10 PC prints one page; then on the host Exit PrintVect from the tray (or pull its network cable). | Within about 30 seconds the Windows 10 PC's job turns yellow "Waiting": "<host> stopped answering after it received this job (switched off or disconnected?), so it may not have printed. If the page did not come out, press Retry waiting jobs when <host> is back." |
| 13 | Start PrintVect on the host again (or plug the cable back in), **Resume printer**. Windows 10 PC: **Retry waiting jobs**. | The page prints exactly once. After Exit, the host had lost the job and prints the resent one. With the cable, the host prints the job it kept on Resume and answers the Retry with "Already printed on <host> ...; not printed again." |
| 14 | Any PC: Settings, turn **Start PrintVect with Windows** off; Yes to Windows. Sign out and in. Then turn it on again (Yes). | The status bar says "PrintVect no longer starts with Windows on this PC..."; after signing in there is no PrintVect in the tray (start it from the Start menu). Turned on again, it starts with Windows as before. |
| 15 | Settings, Keep sent files for: press + twice. Exit PrintVect and start it again. | It says "3 hour(s)" before and after the restart. Press − twice to go back to 1. |
| 16 | Settings, turn off **Ask how many copies**; print a page. Turn it on again. | No copies window while it is off. |
| 17 | Host: Settings, Diagnostics, Refresh. | Under the host section: "PCs that use this PC: ..." with each PC's address, Windows and PrintVect version, and "PAUSED" after a shared printer that is paused. |

What to paste back after M6b-2: whether each step matched; for any step that did not, the Diagnostics text of
both PCs (it ends with the log lines that explain it).

Known limits of M6b-2, by design: an update waits while jobs are queued for a paused printer (Resume first); the
copies window answers by itself after a minute; the ports can only be changed in the settings file.

Release v0.4.0 on 2026-10-17 (Run workflow from main): **released, not tested.** The owner chose to close M6b-2
without running these steps (the report came within a minute of the release, before any PC had downloaded it).
Until they are run, nothing here is verified on Windows: run steps 1 to 17 when the PCs are on 0.4.0.

## 0.4.1: cancel, retry and clear jobs

Asked for by the owner on 2026-10-21 (screenshot of the Jobs page with a waiting job among many printed ones).

| # | Do this | Expect this |
|---|---------|-------------|
| 1 | Windows 10 PC: Exit PrintVect on the host (tray, Exit), print two pages to the host's printer. Jobs page. | Both jobs turn yellow "Waiting" after about 30 seconds; **Cancel all waiting** is now clickable. |
| 2 | Right-click the first waiting job. | A dark menu: **Retry**, **Cancel job**, **Remove from list** (greyed), **Copy details**. |
| 3 | Choose **Cancel job**. | It turns grey "Cancelled"; the status bar says it will not be printed. Use shared printers shows one waiting job less. |
| 4 | Start PrintVect on the host again. Right-click the other waiting job, **Retry**. | It prints. Only that one job was sent (the cancelled one never prints). |
| 5 | Print two more pages while the host is closed again, then press **Cancel all waiting** and **Yes**. | Both turn grey "Cancelled"; "2 waiting job(s) cancelled." |
| 6 | Press **Clear list**, **Yes**. | Every printed, failed and cancelled job leaves the list; jobs still waiting or printing stay. The button greys out. After Exit and a new start the list is still short. |
| 7 | Right-click any job, **Copy details**, paste into Notepad. | Time, document, printer, PC, state, message and the job id, one per line. |
| 8 | Host: Pause printer; Windows 10 PC prints a page. On the host's Jobs page right-click that received job, **Cancel job**. Resume printer. | Host: the job turns red "Failed" with the message "Cancelled on <host> by the person at that PC; not printed.", and Resume prints nothing. Windows 10 PC: the same red row and message within a minute. |
| 9 | Host: right-click a job that is printing right now (or one inside a stuck printer), **Cancel job**. | A message says it is already with the printer and how to open the Windows print queue. |
| 10 | Look at the Host PC column of Sent from this PC. | Every PC name is in the same typewriter-like font (before 0.4.1 every other row was in the normal font). |
| 11 | Share my printers, the Share column. Click in the empty part of a cell beside the box (not on it), then press Space. | The boxes are bigger and all in the middle of their column (before, every other one sat on the left). A shared printer shows a green box with a dark tick, an unshared one an empty box with a grey outline. The click ticks it, Space unticks it, and both are saved (they stay after Exit and a new start). |

## 0.5.0: the redesign, colours and the hotspot fix

Asked for by the owner on 2026-10-22: build the redesign canvas ("PrintVect Redesign"), with a switch for Dark, Light
and System default, together with the fix for printing over a phone hotspot (a hotspot is a Public network in
Windows; PrintVect's firewall rules let other PCs in on Private networks only, and the host gets a new address on
each connection). Steps 16 to 19 need 0.5.0 on both PCs; the rest works PC by PC.

| # | Do this | Expect this |
|---|---------|-------------|
| 0 | Tell me to publish v0.5.0. On each PC: Settings, Check now, Update now. | The menu on the left ends with "Version 0.5.0 · up to date". |
| 1 | Open PrintVect on a PC whose Windows is in light mode (the usual). | Light window: a grey-green menu on the left with the logo, a white box "THIS PC" with the PC name and a "Sharing on" (green) or "Sharing off" (grey) pill, then Office printers, Share my printers, Jobs, Settings. |
| 2 | Settings, General, **Colours**: press **Dark**, then **Light**. | Each press turns the whole window dark or light at once (the title bar too on Windows 10 / 11), and Settings stays open. |
| 3 | Press **System default**. In Windows: Settings, Personalisation, Colours, "Choose your app mode": **Dark**. Then back to **Light**. | PrintVect turns dark within a second or two without a restart, then light again. |
| 4 | Pick Dark, Exit PrintVect (tray, Exit), start it again. Then pick what you prefer. | It starts dark: the choice is kept (config.json: "Theme"). |
| 5 | Client PC: **Office printers**. | "Added to this PC" with a count; one row per printer: green "Ready" pill after a few seconds (grey "Looking…" first, "Not answering" when its PC is off), "N waiting", "Shared by <PC>", "In Print windows: PrintVect - … @<PC>", **Test page**, **Retry waiting** (grey when nothing waits), red **Remove**. |
| 6 | Press **Test page**. | A short message bottom right ("The test page was handed to…"); the page prints on the host. |
| 7 | Look at "Found in the office, not added yet"; press **Search again**. | Printers of other PCs that are not on this PC yet, each with **Add to this PC**; or "Nothing new to add" with how many printers were found. "Last checked" shows the time. |
| 8 | Under **Find a PC by name** type the host's name, press **Look up**. | A box "<HOST> shares N printer(s)": each printer with "Already on this PC" (green tick) or **Add to this PC**. On a PC with two network cards a blue note says so. |
| 9 | Host: **Share my printers**, **Turn off sharing**, then **Turn on sharing**. | Off: grey "Off" pill, "Sharing off" in the menu. Turn on opens "Share printers with the office?": the printers with ticks (the shared ones ticked), Office PIN (recommended) typed twice. Tick one, leave the PIN empty, **Turn on sharing**: green "On", "Sharing on" in the menu. |
| 10 | Look at the printer list. | PrintVect's own printers ("PrintVect - … @<PC>") are not in it; the note under the list names them. Each printer shows its kind ("USB printer · default printer of this PC", "Software printer · saves a PDF file"). |
| 11 | Change "Name other PCs see" of a shared printer, press Enter. On a client, Office printers. | The client shows the new name within about 10 seconds (under Found, or after Search again). |
| 12 | Press **Pause** on a shared printer, then **Resume**. | Yellow "Paused" and the button says **Resume**; back to green "Ready". As in 0.4.0, jobs wait while paused. |
| 13 | Without an office PIN: the yellow note "No office PIN…" on Share my printers, press **Set a PIN**. | Settings opens; Security shows "Not set" (yellow) and **Set PIN…**. |
| 14 | **Jobs**: switch between **Sent from this PC** and **Received from other PCs**; right-click a job. | One list at a time, newest first: time, document (the message in grey under it), printer, PC, copies, a coloured status pill. Counts "Printed today · Waiting". The menu of 0.4.1 (Retry, Cancel job, Remove from list, Copy details). An empty list says what to do and has a button to the right page. |
| 15 | **Settings**, Diagnostics. Press **Show full report**, then **Copy report**, paste into Notepad. | A checklist with green ticks (Windows, firewall rules, folders, network, sharing) and yellow marks for what needs doing (no office PIN). The report as before; it now also has "Network categories" (each network Public or Private). |
| 16 | Hotspot, host side: connect the host's Wi-Fi to a phone hotspot (sharing on). Open Share my printers. | A yellow note: "Windows treats the network "<phone>" as public, so other PCs can't reach this PC's printers over it…" with **Make it private**. Settings, Diagnostics: "Network "<phone>" is public". |
| 17 | Press **Make it private**, answer **Yes** to Windows. | A message "“<phone>” is now a private network…"; the note goes away (at once or within 30 seconds). In Windows: Settings, Network, the Wi-Fi: Network profile type **Private**. |
| 18 | Hotspot, client side: connect the client to the same hotspot and print a page to the host's printer. | It prints. |
| 19 | Switch the phone's hotspot off and on (both PCs reconnect and the host usually gets a new address). Print again from the client without opening Office printers. | It prints. Jobs shows "Looking for <host> on the network" for a moment if the old address no longer answers; the client's log has "<host> now answers at <new address> (was <old>); sending there." |
| 20 | Make the window narrow (drag its edge). | Buttons move under the text; on Share my printers the right column goes under the printer list. Nothing is cut off; the page scrolls. |

What to paste back after 0.5.0: whether each step matched; for a step that did not, a photo of the screen and the
Diagnostics report of the PC (Copy report).

Known limits of 0.5.0: **Make it private** needs the Windows permission question (Elevate helper), and a company
policy can stop the change (the message says so: ask whoever looks after the PC). Some phone hotspots keep the
devices on them apart ("client isolation"); then PCs on it cannot reach each other at all, whatever PrintVect does:
use the office network or another hotspot. The copies window and the PIN window take the colours in use when they
open.

## 0.5.1: pages open without jumping

Reported by the owner on 2026-10-22: moving between Office printers, Share my printers, Jobs and Settings showed
jitters and glitches. The cause: a page that is not open keeps the size it had when last seen, and opening it laid
out every row again (dozens of passes) while Windows was already drawing it, so the half-finished steps reached the
screen; the Diagnostics checklist was also only collected when Settings opened, so its card grew a moment later.
Now drawing is paused while a page is sized and laid out and the finished page is drawn once; Diagnostics is
collected when the window opens.

| # | Do this | Expect this |
|---|---------|-------------|
| 0 | Tell me to publish v0.5.1. On each PC: Settings, Check now, Update now. | "Version 0.5.1 · up to date" under the menu. |
| 1 | Click Office printers, Share my printers, Jobs, Settings, then back, several times, quickly and slowly. | Each page appears complete in one go: no rows sliding into place, no flashing of the old page, no buttons jumping. |
| 2 | Exit PrintVect (tray, Exit), start it again, open the window and go straight to **Settings**. | The Diagnostics checklist is already filled in (or fills in within a second if you are very quick); the card does not jump. |
| 3 | Make the window bigger (maximise), go to another page, then make it smaller again and go back. | The page appears at the new size at once, without rearranging itself on screen. |
| 4 | Press Ctrl+Tab a few times while the cursor is in the "PC name or IP address" box on Office printers. | The pages change in turn; Settings opens at its top (it does not scroll down by itself). |
| 5 | Settings, Updates: wait for "PrintVect is up to date" (or press Check now). | The green tick sits just left of the text, with a small gap; with any other message there is no tick and no empty space. |
| 6 | Repeat step 1 in Dark colours (Settings, Colours, Dark). | The same. |

If a page still flickers, say which page and whether it is the first time it is opened after a start or every
time; a short phone video helps.

## 0.5.2: the owner's look-and-feel list

Reported by the owner on 2026-10-23, one issue at a time.

1. Opening PrintVect from the tray showed a grey window full of empty white boxes for a moment (photo). Windows puts a
   window on the screen before the program has drawn what is inside it, and on the first opening PrintVect still has
   to create and draw about 150 parts of the window. Now the window is kept off the screen (cloaked) until everything
   in it is drawn, then appears in one go (Windows 8 and later).

| # | Do this | Expect this |
|---|---------|-------------|
| 0 | Tell me to publish v0.5.2. On each PC: Settings, Check now, Update now. | "Version 0.5.2 · up to date" under the menu. |
| 1 | Exit PrintVect (tray, Exit), start it from the Start menu, close the window (it goes to the tray), then double-click the tray icon. Repeat a few times, also with Light and Dark colours. | The window appears complete at once: no grey window with white boxes. The first time after a start it may take a moment longer to appear. |
| 2 | Minimise the window (the _ button), then open it from the tray icon. | It comes back complete at once. |

## Later

Phase 2 items (brief section 9): raw port-9100 relay, Hindi UI, optional service host, job counter per user.
