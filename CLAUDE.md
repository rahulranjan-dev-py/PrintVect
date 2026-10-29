# PrintVect - CLAUDE.md

## What this is
Windows tray app that shares printers across a LAN of Windows 10 / 11 PCs (Windows 7 / 8 by construction only,
untested) without drivers or credentials on the client. Full brief (written under the working name MudranSetu):
docs/brief.md. Wire protocol: docs/protocol.md. Manual tests: docs/testing.md.

## Hard rules (never change without asking the owner)
- C# on .NET Framework 4.8 only (LangVersion 7.3). WinForms. AnyCPU, Prefer 32-bit OFF.
- No NuGet in shipped code except Newtonsoft.Json. Test-only packages: MSTest. Ask before adding anything.
- Printing and discovery are LAN only. The one internet call (owner's request, 2026-10-09) is the update check:
  Core/Update/UpdateChecker asks GitHub Releases of this repo over HTTPS (daily + Check now, off switch in Settings),
  downloads PrintVect-Setup-<v>.exe, verifies size + .sha256, runs it /SILENT (UAC prompt; the setup ends the app
  and restarts it). No telemetry, no crash reporting, nothing else leaves the office.
- Host role runs as a tray app in the logged-in user's session, never as a Windows Service
  (System.Printing does not work in Session 0).
- Ports: UDP 9150 discovery, TCP 9151 jobs. Configurable, but never 9100 or 631.
- Protocol identifiers: discovery text `PVECT-DISCOVER 1`, TCP magic `PVCT`, `"app":"PrintVect"`.
- All admin work lives in src/PrintVect.Elevate (requireAdministrator manifest, launched with
  ShellExecute "runas"). PrintVect.App is asInvoker and runs as a standard user.
- Data in %ProgramData%\PrintVect\ : config.json, spool\, logs\ (daily files, keep 14 days), jobs-host.json and
  jobs-client.json (job history, last 200, saved a second after a change), updates\ (downloaded setup + its log).
- All user-visible text lives in src/PrintVect.App/Strings.resx from day one (Hindi is Phase 2).
- MIT licence (LICENSE in repo root).

## Architecture
- src/PrintVect.Core     class library: Config/, Logging/, Diagnostics/, Protocol/, Host/, Printing/,
                         Client/ (spool watcher, sender), Elevation/, Discovery/, Update/. No UI references.
- src/PrintVect.App      WinForms tray exe in the owner's 0.5.0 redesign (docs/design/design-spec.md): side menu with Printers on other PCs,
                         Share my printers, Jobs, Settings. Theme/: painted controls, Palette (Light + Dark), ThemeManager (Light /
                         Dark / System default; a change rebuilds the window). An opened page stays laid out behind the open one
                         (brought to front, IPage.SetOpen; 0.5.2); Reveal keeps the window cloaked until drawn. Close = tray.
- src/PrintVect.Send     pvct-send.exe, the M1 command-line test sender (list / send / status). docs/samples/
                         PrintVect-test-page.xps is a ready-made one-page test file (tools/make_test_page.py rebuilds it).
- src/PrintVect.Elevate  tiny console exe, requireAdministrator. Commands add-printer, remove-printer, remove-all
                         (XcvData AddPort/DeletePort on the Local Port monitor, AddPrinter, DeletePrinter, Users:Modify ACL
                         on spool\<id> via a handle; SafeFolders refuses junctions/links in the data folder, 0.5.8; when no XPS writer driver is installed it installs Windows' own from the driver store
                         (InstallPrinterDriverFromPackage) or turns on the feature with DISM Printing-XPSServices-Features);
                         autostart /on yes|no (HKLM Run); network-private /networks <ids> (0.5.0). Result: exit code + JSON.
- tests/PrintVect.Tests  MSTest, net48. Unit-tests framing, discovery JSON, config, file-stability, updates (loopback HTTP).
- installer/PrintVect.iss Inno Setup 6, compiled by CI (ISCC, /DAppVersion from Directory.Build.props, /DSourceDir =
                         collected program files): Program Files, Users:Modify on ProgramData, netsh firewall rules, HKLM Run
                         /tray, uninstall = Elevate remove-all, rules and data folder gone. taskkill without /T (the setup is
                         the app's child during an update); "Start PrintVect now" also runs after a silent update,
                         runasoriginaluser. .github/workflows/release.yml (tag v<Version> or Run workflow) publishes
                         PrintVect-Setup-<v>.exe + .sha256 as a GitHub Release. docs/design/: icon SVG (tools/make_icon.py)
                         and visual spec; src/PrintVect.App/Fonts: OFL fonts (static Noto Sans from tools/make_fonts.py).

Client: virtual printer "PrintVect - <friendly> @<host>" (XPS Document Writer driver, the v3 one when installed
because it writes .xps; v4 writes .oxps; Local Port -> spool\<id>\job.xps) -> SpoolWatcher (FileSystemWatcher + 2 s
polling, re-checks within 0.5 s; taken when exclusively openable and size stable 0.5 s) -> rename to job-<guid>.xps/.oxps
(XpsFormatSniffer looks inside: namespace openxps.org = oxps; it also reads the docProps title; the v4 writer stores
none, so the document name comes from EnumJobs on the virtual printer while the port writes) -> ClientService sends
(3 tries, each IP then PC name; after the first failure one 3 s discovery search follows a host that moved (0.5.0); a new address or the name's
address is used only once it signs a challenge with the host key remembered for the printer (0.5.8, HostIdentity, MovedHostCheck); then pending\
and Retry resends under the same job id; sent\ kept 1 h, failed\ last 20) and polls status.
Host: receive into spool\incoming\ -> verify size, sniff the real format -> HostPrintEngine -> reply JSON -> watch the
job (up to 60 s before replying). One job at a time per printer, arrival order, on that printer's worker thread (never
the UI thread). Printer statuses and printing sit behind IPrinterStatusSource / IPrintEngine so HostService is
unit-tested with fakes over loopback TCP (tests never load System.Printing, which Mono lacks).

## Protocol v1 (docs/protocol.md has the JSON)
- Discovery: client broadcasts `PVECT-DISCOVER 1` to 255.255.255.255:9150 every 10 s while the Printers on other PCs
  page is open, and once when a job cannot reach its host; hosts reply by UDP unicast with a JSON printer list.
- Job: TCP 9151, one job per connection: `PVCT` + 4-byte LE header length + UTF-8 JSON header + file bytes. Host
  answers one JSON line ({ok, jobId, state, message}) and closes. Status: header {"type":"status","jobId"}, no body.
- List: header {"type":"list"}, reply = discovery JSON (with hostKey; hostSig when the request has a challenge, 0.5.8) + ok. Hello (M6b-2): {"type":"hello",client,os,appVersion,
  printers} every 5 min to each host used -> its "PCs that use this PC". Refusals: drained, ok=false; "retry":true =
  keep for Retry (lost job, PIN). A job id already printed or still queued gets its state back, never printed twice.
- Sharing PIN (0.5.8, PinProof): job and hello carry nonce, time, pinProof = PBKDF2-SHA256(PIN, salt nonce/time/printer/jobId/size, 20000);
  time within 24 h; the old plain hash "pin" is refused; 5 wrong per address -> wait 15 min, doubling to 4 h (PinAttemptLimiter).
  Empty PIN = open on the LAN. Limits: file 200 MB, connect 5 s, header 15 s, transfer 5 min, discovery 2 s; host (0.5.8, ConnectionLimiter): 32 connections, 8 per address, 120/min per address; incoming <= 1 GB and 256 MB disk free (unused files evicted first, else retry), non-XPS deleted, unused files gone after 24 h (hourly). Manual IP/name entry always exists.

## Workflow
- One milestone at a time (M0..M6 in the brief, section 9). Finish, build, hand over test steps,
  then wait for the owner's result before starting the next one. M4 (Windows 7 pass) is skipped:
  the office has no Windows 7 PC (owner, 2026-10-06); keep Windows 7 paths only where they cost nothing. M6: M6a
  updates, M6b-1 new look, M6b-2 hardening; then 0.4.1 (jobs menu), 0.5.0 (redesign, colours, hotspot), 0.5.1-0.5.3 (look fixes), 0.5.4, 0.5.5 (no office words: homes too), 0.5.6 (menu fits), 0.5.7 (copies window), 0.5.8 (security review: PIN proof, host identity, Elevate link guard, host flood limits).
- Build: `msbuild PrintVect.sln /p:Configuration=Release` (VS 2022 Build Tools + .NET Framework 4.8 targeting pack); `dotnet build PrintVect.sln -c Release` (.NET 8 SDK) too.
- Tests: `dotnet test tests/PrintVect.Tests/PrintVect.Tests.csproj -c Release`. CI: .github/workflows/build.yml (windows-latest) builds,
  tests, makes the setup program; release.yml publishes a release (Run workflow on main).
- Claude Code cannot see a printer or run Windows print APIs: design every feature so the owner can verify it from a log file or Diagnostics, and log every job step with its jobId on both sides.
- Commit after each milestone: `M2: client virtual printer and spool watcher`.
- Files under %ProgramData% belong to the user who created them: the installer grants Users modify rights on
  %ProgramData%\PrintVect, and the logger still falls back to PrintVect-<user>-<date>.log if a file is not writable.
- The owner's Windows 11 test PC has two Ethernet cards on different subnets (172.16.x and
  192.168.10.x), so DiscoveryClient binds one UDP socket per IPv4 address and sends to each card's directed
  broadcast and 255.255.255.255; DiscoveryResponder binds 0.0.0.0:9150 and answers from the address
  on the asker's subnet (LocalNetworks.BestLocalAddressFor). Discovery runs while the Printers on other PCs
  page is visible; a typed name goes through the TCP list request and joins the same found list.
- Firewall rules allow private and domain networks only (brief 10); a phone hotspot comes up Public, so Share my printers
  warns (Core NetworkProfiles: Network List Manager COM) and Make it private runs Elevate network-private (0.5.0).
- Seen on the owner's Windows 11 host: PrintQueue.AddJob(fastCopy:false) worked for Microsoft Print to PDF
  (returning only after the Save dialog, job already gone from the queue) but hung forever for the USB
  "HP Laser 103 107 108" without ever creating a spooler job. The XPS Print API (XpsPrintEngine) then failed
  every job on that PC with 0x80040003 OLE_E_ADVISENOTSUPPORTED before a spooler job existed: deprecated and
  unreliable there. The host therefore prints .xps by handing the file to the spooler with plain winspool calls (SpoolerXpsEngine: OpenPrinter,
  GetPrinterDriver level 8 to pick XPS_PASS for XPS-based drivers (v4 / attribute 0x2) or XPS2GDI
  for GDI drivers, StartDocPrinter, WritePrinter, EndDocPrinter, GetJob level 2 polling with the
  Windows status text in the log). XpsPrintEngine and SystemPrintingEngine are fallbacks for start
  failures only; .oxps goes the same way on Windows 8+ (verified on paper 2026-10-05), System.Printing on
  Windows 7. PORTPROMPT:/FILE: printers (Microsoft Print to PDF) use System.Printing: under XPS_PASS the PDF
  driver saved an unreadable file with no Save window, AddJob showed the window and made a good PDF.
  docs/samples has PrintVect-test-shapes.xps (no font) to tell a document problem from a printer one. XpsPrintEngine
  (fallback only): the job object refused QueryInterface for IXpsPrintJob there, so it calls Write/Close/GetJobStatus
  through raw COM function tables on an MTA thread; the empty print ticket stream MUST be closed before the document is written.
  System.Printing's XPS path needs STA: each engine runs through ApartmentRunner on its apartment. PrintDispatcher runs one worker per printer;
  HostService reports a job stuck after 20 min and retires that worker. "Ethernet 2" on that PC is a phone tethered by USB (address changes
  per session); "Ethernet" 192.168.10.x is the office LAN; the HP Laser is on USB001.
- Ask before: new dependency, framework change, port change, data-folder change, anything needing admin
  outside the Elevate helper, any internet use beyond the update check, a Windows Service.
- When the owner pastes an error or log: restate what happened in one sentence, then propose the smallest fix.

## Style
- Small classes, one responsibility each. Async/await for network and file I/O; never block the
  UI thread; never call PrintServer on the UI thread.
- No silent catches: every catch logs the exception with jobId and context, then surfaces a
  plain-language message that says what to do next ("Ask the Counter 1 PC to be switched on").
- Own minimal logger in Core (daily rolling file, 14-day retention). No logging frameworks.
- Show PC names, not IPs, wherever possible. Users are office and home users, not IT staff.
