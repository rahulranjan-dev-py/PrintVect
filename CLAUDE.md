# PrintVect - CLAUDE.md

## What this is
Windows tray app that shares printers across a LAN of mixed Windows 7 / 8 / 10 / 11 PCs
without drivers or credentials on the client. Full brief (written under the working name
MudranSetu; PrintVect is the chosen name): docs/MudranSetu_Claude_Code_Guide.pdf.
Wire protocol with the PrintVect identifiers: docs/protocol.md. Manual tests: docs/testing.md.

## Hard rules (never change without asking the owner)
- C# on .NET Framework 4.8 only (LangVersion 7.3). WinForms. AnyCPU, Prefer 32-bit OFF.
- No NuGet in shipped code except Newtonsoft.Json. Test-only packages: MSTest. Ask before adding anything.
- LAN only. No internet calls, no telemetry, no crash reporting, no auto-update.
- Host role runs as a tray app in the logged-in user's session, never as a Windows Service
  (System.Printing does not work in Session 0).
- Ports: UDP 9150 discovery, TCP 9151 jobs. Configurable, but never 9100 or 631.
- Protocol identifiers: discovery text `PVECT-DISCOVER 1`, TCP magic `PVCT`, `"app":"PrintVect"`.
- All admin work lives in src/PrintVect.Elevate (requireAdministrator manifest, launched with
  ShellExecute "runas"). PrintVect.App is asInvoker and runs as a standard user.
- Data in %ProgramData%\PrintVect\ : config.json, spool\, logs\ (daily files, keep 14 days).
- All user-visible text lives in src/PrintVect.App/Strings.resx from day one (Hindi is Phase 2).
- MIT licence (LICENSE in repo root).

## Architecture
- src/PrintVect.Core     class library: Config/, Logging/, Diagnostics/, later Protocol/, Discovery/,
                         Spool/, Printing/. No UI references, no System.Windows.Forms.
- src/PrintVect.App      WinForms tray exe. Tabs: Share my printers, Use shared printers, Settings,
                         Diagnostics. Closing the window minimises to tray; Exit is in the tray menu.
                         `/tray` switch starts hidden (used by the autostart entry).
- src/PrintVect.Send     pvct-send.exe, the M1 command-line test sender (list / send / status).
                         docs/samples/PrintVect-test-page.xps is a ready-made one-page test file
                         (tools/make_test_page.py rebuilds it).
- src/PrintVect.Elevate  tiny console exe, requireAdministrator. Only creates/removes printers,
                         ports and firewall rules. Results come back as exit code + JSON file.
- tests/PrintVect.Tests  MSTest, net48. Unit-tests framing, discovery JSON, config, file-stability.
- installer/PrintVect.iss Inno Setup 6 (milestone M5).

Client: virtual printer (Microsoft XPS Document Writer driver, Local Port -> file in spool\<id>\)
-> FileSystemWatcher + 2 s polling fallback -> rename to job-<guid>.xps -> TCP send.
Host: receive into spool\incoming\ -> verify size -> XPS Print API (StartXpsPrintJob) for .xps,
PrintQueue.AddJob for .oxps -> reply JSON -> watch the job (up to 60 s before replying). One job at a time per printer, arrival
order, on that printer's worker thread (never the UI thread). Printer statuses and
printing sit behind IPrinterStatusSource / IPrintEngine so HostService is unit-tested with fakes
over loopback TCP (tests never load System.Printing, which Mono lacks).

## Protocol v1 (docs/protocol.md has the JSON)
- Discovery: client broadcasts `PVECT-DISCOVER 1` to 255.255.255.255:9150 every 10 s while the
  Use tab is open; hosts reply by UDP unicast with a JSON printer list.
- Job: TCP 9151, one job per connection: `PVCT` + 4-byte LE header length + UTF-8 JSON header
  + file bytes. Host answers one JSON line ({ok, jobId, state, message}) and closes.
- Status: same framing, header {"type":"status","jobId":...}, no body.
- List: same framing, header {"type":"list"}, no body; reply = discovery JSON + ok. Added in M1 for
  pvct-send and for manual "Add by IP" (M3). Refused jobs are drained, then answered with ok=false.
- Optional PIN: header carries SHA-256 hex of the PIN; empty = open on the LAN.
- Limits: file 200 MB, connect 5 s, transfer 5 min, discovery 2 s. Manual IP/name entry always exists.

## Workflow
- One milestone at a time (M0..M6 in the PDF, section 9). Finish, build, hand over test steps,
  then wait for the owner's result before starting the next one.
- Build: `msbuild PrintVect.sln /p:Configuration=Release` (VS 2022 Build Tools + .NET Framework
  4.8 targeting pack). `dotnet build PrintVect.sln -c Release` also works with the .NET 8 SDK.
- Tests: `dotnet test tests/PrintVect.Tests/PrintVect.Tests.csproj -c Release`.
- CI: .github/workflows/build.yml on windows-latest builds, tests and uploads the EXEs.
- Claude Code cannot see a printer or run Windows print APIs. Design every feature so the
  owner can verify it from a log file or the Diagnostics tab, and log every job step with
  its jobId on both sides.
- Commit after each milestone: `M2: client virtual printer and spool watcher`.
- Files under %ProgramData% belong to the user who created them. The M5 installer must grant
  Users modify rights on %ProgramData%\PrintVect; until then the logger falls back to a
  per-user file name (PrintVect-<user>-<date>.log) so a second user can still start the app.
- The owner's Windows 11 test PC has two Ethernet cards on different subnets (10.169.x and
  10.148.x). Discovery (M3) must send the broadcast on every interface, not only to
  255.255.255.255 once, and the host must listen on all IPv4 addresses.
- Seen on the owner's Windows 11 host: PrintQueue.AddJob(fastCopy:false) worked for Microsoft
  Print to PDF (returning only after the Save dialog, job already gone from the queue) but hung
  forever for the USB "HP Laser 103 107 108" without ever creating a spooler job. The host therefore
  prints .xps with the XPS Print API (XpsPrintEngine: xpsprint.dll, any printer, non-blocking,
  page progress, completion event) and keeps System.Printing (SystemPrintingEngine) only for
  .oxps and as a fallback when the API refuses to start a job (HostPrintEngine decides).
  The XPS Print API works only from an MTA thread (E_NOINTERFACE from STA, seen on the owner's
  PC) while System.Printing's XPS path needs STA: each engine runs its call through
  ApartmentRunner on the apartment it needs. PrintDispatcher runs one worker per printer;
  HostService reports a job stuck after 20 min and retires that worker. "Ethernet 2" on that PC is a phone tethered by USB (address changes
  per session); "Ethernet" 10.148.93.x is the office LAN; the HP Laser is on USB001.
- Ask before: new dependency, framework change, port change, data-folder change, anything
  needing admin outside the Elevate helper, anything needing internet, a Windows Service.
- When the owner pastes an error or log: restate what happened in one sentence, then propose
  the smallest fix.

## Style
- Small classes, one responsibility each. Async/await for network and file I/O; never block the
  UI thread; never call PrintServer on the UI thread.
- No silent catches: every catch logs the exception with jobId and context, then surfaces a
  plain-language message that says what to do next ("Ask the Counter 1 PC to be switched on").
- Own minimal logger in Core (daily rolling file, 14-day retention). No logging frameworks.
- Show PC names, not IPs, wherever possible. Users are postal clerks, not IT staff.
