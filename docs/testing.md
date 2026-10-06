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

Copies (seen 2026-10-06): printing 2 copies from Word to a PrintVect printer gave one page, because the
XPS writer records the copies count inside the file instead of repeating the pages. From build 26 the client
reads that count from the file's print ticket and the host prints the document that many times (each copy its
own Windows job); the job list shows "2 copies". Chrome hides the Copies field for the XPS writer altogether,
so Chrome users still print twice until M6 adds PrintVect's own copies prompt.

Known limits at M3, by design: the found list only shows hosts on this PC's own subnet(s); a host that
changes its IP address is followed automatically only while the Use tab is open (otherwise the next
job tries the IP, then the PC name); the Settings tab is still read-only (M6).

What to paste back after M3: whether each step matched, and the Diagnostics text of both PCs.

## M4 and later

Steps are added here when each milestone is delivered.
