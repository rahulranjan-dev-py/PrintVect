# PrintVect

PrintVect lets every PC in a small office print to every printer in the office, whatever
version of Windows each PC runs (Windows 7 SP1 through Windows 11), with **no driver
installation and no Windows user name or password on the client side**.

It exists because built-in Windows printer sharing does not work across a mix of Windows 7,
8, 10 and 11 machines: drivers differ per Windows version, Windows 7 shares over SMB1 which
newer Windows disables, and the PrintNightmare hardening makes every connection ask for an
administrator. PrintVect side-steps all of that:

- The PC that has the printer (the **host**) runs PrintVect in the tray and offers the printer
  to the office.
- Any other PC (a **client**) adds that printer as a *virtual printer* which uses the Microsoft
  XPS Document Writer driver that every Windows from 7 to 11 already has.
- When someone prints, the client sends the job file over the office network to the host, and
  the host prints it with its own, already-working driver.

Everything stays on the office LAN. PrintVect never contacts the internet.

## Status

Work is done in milestones (see `docs/MudranSetu_Claude_Code_Guide.pdf`, section 9; MudranSetu
was the working name before PrintVect was chosen).

| Milestone | What it delivers | State |
|-----------|------------------|-------|
| M0 | Solution scaffold, CI build, empty tray app with Diagnostics tab | **done, verified on Windows 11** |
| M1 | Host role: printer list, share toggle, job listener, printing, `pvct-send` test tool | next |
| M2 | Client role on Windows 10/11: virtual printer, job watcher, status balloons | |
| M3 | Discovery, Add/Remove printers UI, manual IP entry, job history | |
| M4 | Windows 7 pass (32-bit, v3 XPS driver, .oxps handling) | |
| M5 | Installer (Inno Setup): .NET 4.8 check, firewall rules, autostart, clean uninstall | |
| M6 | Hardening: PIN, retries, cleanup, log rotation, plain-language errors | |

## Requirements

- Windows 7 SP1, 8, 8.1, 10 or 11 (32-bit or 64-bit).
- .NET Framework 4.8. Windows 10 (May 2019 update or later) and Windows 11 already have it.
  Windows 7 and 8.1 need the offline installer `ndp48-x86-x64-allos-enu.exe`; Windows 7 also
  needs SP1 plus updates KB4474419 and KB4490628 first.
- All PCs on the same office network.

## Getting the program (until the installer exists)

1. Open the **Actions** tab of this repository on GitHub and pick the latest green run.
2. Download the artifact `PrintVect-build-<number>` and unzip it into a folder such as
   `C:\PrintVect`.
3. Run `PrintVect.App.exe`. A PrintVect icon appears in the tray (bottom-right, near the clock)
   and the PrintVect window opens.

Windows SmartScreen may warn because the program is not yet signed: click **More info**, then
**Run anyway**.

Or build it yourself: install Visual Studio 2022 Build Tools with the ".NET Framework 4.8
targeting pack", then run in the repo folder:

```
msbuild PrintVect.sln /t:Restore /p:Configuration=Release
msbuild PrintVect.sln /p:Configuration=Release
tests\PrintVect.Tests\bin\Release\net48\PrintVect.Tests.exe
```

`dotnet build PrintVect.sln -c Release` with the .NET 8 SDK works too.

## Using it

The window has four tabs:

- **Share my printers**: the printers on this PC and whether they are offered to the office (M1).
- **Use shared printers**: printers found on other PCs; add them to this PC (M3).
- **Settings**: shared PIN, ports, start with Windows, how long to keep sent files.
- **Diagnostics**: press **Copy to clipboard** and paste the text into an email or chat when
  something does not work. It contains the Windows version, .NET version, network addresses,
  firewall rule state, printer list and the last 200 log lines. Nothing is sent automatically.

Closing the window only hides it; PrintVect keeps running in the tray. To stop it, right-click
the tray icon and choose **Exit**. `PrintVect.App.exe /tray` starts it hidden (the installer's
autostart entry uses this).

## Where things are

| Item | Location |
|------|----------|
| Settings | `C:\ProgramData\PrintVect\config.json` |
| Job files in transit | `C:\ProgramData\PrintVect\spool\` |
| Logs (one per day, kept 14 days) | `C:\ProgramData\PrintVect\logs\PrintVect-<date>.log` |
| Admin helper log | `C:\ProgramData\PrintVect\logs\PrintVect-Elevate-<date>.log` |
| Ports | UDP 9150 (finding printers), TCP 9151 (sending jobs) |

`ProgramData` is a hidden folder; type the path into the Explorer address bar.

## Troubleshooting

| Problem | What to do |
|---------|------------|
| "PrintVect could not create its data folder" at start | Ask whoever installed PrintVect to check that `C:\ProgramData\PrintVect` can be written to. |
| No tray icon after starting | Check the hidden icons arrow next to the clock. If still nothing, look for the newest file in `C:\ProgramData\PrintVect\logs\`. |
| Antivirus on Windows 7 quarantines the EXE | Add the PrintVect folder and `C:\ProgramData\PrintVect` to its exclusions; the program is unsigned for now. |
| Something else | Diagnostics tab, **Copy to clipboard**, and send the text to the person who supports PrintVect. |

## For developers

- `CLAUDE.md` holds the condensed rules; the full brief is in `docs/`.
- `docs/protocol.md` describes the wire protocol; `docs/testing.md` the manual test steps per milestone.
- Every user-visible string lives in `src/PrintVect.App/Strings.resx`. After editing it without
  Visual Studio, run `python3 tools/generate_strings.py`.
- Licence: MIT (see `LICENSE`).
