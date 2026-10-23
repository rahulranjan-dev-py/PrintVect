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

Printing stays on the office LAN. The only thing PrintVect ever fetches from the internet is its own
newer version: once a day it looks at this repository's releases on github.com and offers an update
with one click (Settings page; the daily look can be switched off). Nothing about the office, its
printers or its documents is ever sent anywhere.

## Status

Work is done in milestones (see `docs/MudranSetu_Claude_Code_Guide.pdf`, section 9; MudranSetu
was the working name before PrintVect was chosen).

| Milestone | What it delivers | State |
|-----------|------------------|-------|
| M0 | Solution scaffold, CI build, empty tray app with Diagnostics tab | **done, verified on Windows 11** |
| M1 | Host role: printer list, share toggle, job listener, printing, `pvct-send` test tool | **done, verified on Windows 11 host with a Windows 10 sender** |
| M2 | Client role: virtual printer through the Elevate helper, spool watcher, status balloons, Use tab | **done, verified on Windows 10 and 11 clients** |
| M3 | Hosts found by themselves (UDP discovery on every network card), found-printers list, job history that survives a restart | **done, verified on two Windows 11 PCs and a Windows 10 PC** |
| M4 | Windows 7 pass | **skipped**: no Windows 7 PC in the office; tested on Windows 10 and 11 only |
| M5 | Installer (Inno Setup): Program Files, firewall rules, autostart, clean uninstall, bundled fonts and the new icon | **done, verified on Windows 10 (daily use)** |
| M6a | Updates from inside the program: daily look at the GitHub releases, one-click install, release workflow | **done, verified on three PCs (0.2.0 to 0.2.1 by itself)** |
| M6b-1 | The new look (docs/design): dark Graphite window, bundled fonts, top bar with pages, Jobs page, Diagnostics inside Settings | **done, reported OK by the owner** |
| M6b-2 | Hardening: editable settings and office PIN, copies question, PCs that use a host, Pause / Resume, jobs kept for Retry when a host is lost, plain-language errors | **released in 0.4.0 (2026-10-17), not tested** |
| 0.4.1 | Jobs page: right-click Retry / Cancel job / Remove from list / Copy details, Cancel all waiting, Clear list; bigger green tick boxes on Share my printers | **done, awaiting test** |
| 0.5.0 | The owner's redesign: side menu, Light / Dark / System default colours, Office printers page, Turn on sharing window, PrintVect's own printers can no longer be shared again; the phone hotspot fix (Make it private, a job finds a host's new address by itself) | **released 2026-10-22, awaiting test** |
| 0.5.1 | Pages open in one go: no more jumping or flicker when moving between Office printers, Share my printers, Jobs and Settings | **released 2026-10-23, awaiting test** |
| 0.5.2 | The owner's list of look-and-feel fixes: the window no longer opens from the tray as empty white boxes; pages switch at once | **done, awaiting test** |

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

Because the program is new and not yet signed, browsers and Windows treat it with suspicion:

- Chrome may say "Chrome blocked this download because the file is dangerous". Open the
  downloads list (Ctrl+J), click the three-dot menu on the blocked entry and choose **Download
  dangerous file** (or **Keep**), then confirm. Microsoft Edge shows a similar warning with
  **Keep** under its menu. The digest shown next to the artifact on the Actions page identifies
  the exact file GitHub built from this repository's source.
- Windows SmartScreen may warn when you first run it: click **More info**, then **Run anyway**.
- If Windows Defender removes `PrintVect.App.exe` from the folder, add `C:\PrintVect` to its
  exclusions (Windows Security, Virus & threat protection, Manage settings, Exclusions).

Or build it yourself: install Visual Studio 2022 Build Tools with the ".NET Framework 4.8
targeting pack", then run in the repo folder:

```
msbuild PrintVect.sln /t:Restore /p:Configuration=Release
msbuild PrintVect.sln /p:Configuration=Release
tests\PrintVect.Tests\bin\Release\net48\PrintVect.Tests.exe
```

`dotnet build PrintVect.sln -c Release` with the .NET 8 SDK works too.

## Using it

The window (the owner's redesign of 0.5.0) has a menu on the left: the PC's name and whether it shares its
printers at the top, then four pages, and the version at the bottom. Settings, **Colours** picks **Light**,
**Dark** or **System default** (the light or dark mode of Windows, followed when it changes).

- **Office printers**: the printers of other PCs already on this PC, each with its status, how many jobs wait
  for it, **Test page**, **Retry waiting** and **Remove**; the printers found in the office that are not added
  yet (they appear by themselves; **Add to this PC**, Windows asks once for permission); and **Find a PC by
  name** for a PC that does not appear (type its name or IP address, **Look up**).
- **Share my printers**: **Turn on sharing** opens a window to tick the printers to share and set the office
  PIN; **Turn off sharing** stops it. Below: the name other PCs find this PC by (**Copy PC name**), a yellow
  note when there is no office PIN (**Set a PIN**) or when Windows treats the network as public (**Make it
  private**, see Troubleshooting), and the printers of this PC with a tick, the name other PCs see (for
  example "Counter 1 Laser"), the status and **Pause** / **Resume** (jobs from other PCs wait while a printer
  is paused). Printers of other PCs added here are not listed: they cannot be shared again. On the right: the
  jobs printed today, the **PCs that print here** with their Windows and PrintVect version, and how another PC
  connects.
- **Jobs**: **Sent from this PC** or **Received from other PCs**, newest first, with today's printed and waiting
  counts, the copies, a coloured status and the job's message under the document. Right-click a job for
  **Retry** or **Cancel job** (a waiting job), **Remove from list** (a finished one) or **Copy details**; Delete
  does the same as Cancel or Remove. **Cancel all waiting** cancels every job this PC still has to send, **Clear
  list** removes the finished jobs from the list shown (nothing on any printer changes). A job received here that
  has not reached the printer yet (for example while the printer is paused) can be cancelled on this PC too.
- **Settings**: Security (the office PIN: the same PIN on every PC), General (Colours, start with Windows,
  whether printing asks how many copies, how long to keep sent documents), Updates (a daily look for a newer
  version, **Check now**, **Update now** when one is ready), the network ports, and **Diagnostics**: a checklist
  of what PrintVect checked on this PC (Windows, firewall rules, folders, networks and whether one is public,
  PIN, sharing) and **Copy report** to paste into an email or chat when something does not work. The report
  contains the Windows version, .NET version, network addresses, firewall rule state, printer list and the last
  200 log lines. Nothing is sent automatically.

Ctrl+Tab moves to the next page.

When a program prints to a PrintVect printer, a small window asks **how many copies** before the job leaves the
PC (Windows' XPS writer loses the copies chosen in the program). Enter prints, Esc cancels; after a minute without
an answer it prints the number shown. Settings can turn the question off.

Closing the window only hides it; PrintVect keeps running in the tray. To stop it, right-click
the tray icon and choose **Exit**. `PrintVect.App.exe /tray` starts it hidden (the installer's
autostart entry uses this).

### Installing with the setup program (M5)

Download `PrintVect-Setup-<version>.exe` from the latest entry on the **Releases** page of this
repository (or the `PrintVect-Setup-<n>` artifact of the latest green build on the Actions page, which is
a zip around the same file) and run it. The program is not signed yet, so SmartScreen warns once:
**More info**, then **Run anyway**. Setup installs to `C:\Program Files\PrintVect`, keeps any settings
already in `C:\ProgramData\PrintVect`, opens TCP 9151 and UDP 9150 in Windows Firewall for PrintVect
only, adds a Start menu entry and starts PrintVect with Windows (hidden in the tray). Uninstalling from
Settings, Apps removes the PrintVect printers, the firewall rules, the program and its data folder.

### Using a shared printer from another PC (client role, M2)

On the PC that should print: open PrintVect, **Office printers**, press **Add to this PC** next to the
printer under "Found in the office" (or type the host PC's name or IP address under **Find a PC by name**,
**Look up**, then **Add to this PC**) and answer **Yes** to the Windows permission window. A printer named `PrintVect - <printer> @<host PC>` appears in every program's
print window; what you print on it comes out of the host's printer. `PrintVect.Elevate.exe` is the
only part that runs as administrator: it creates the printer, its port and its spool folder
(`C:\ProgramData\PrintVect\spool\<id>`), and removes them again (**Remove**, or
`PrintVect.Elevate.exe remove-all` from an administrator Command Prompt to remove every PrintVect
printer at once).

### Sending a test job with pvct-send (no virtual printer needed)

`pvct-send.exe` sits next to `PrintVect.App.exe`. From a Command Prompt on any PC that has
.NET Framework 4.8:

```
pvct-send list 192.168.1.5
pvct-send PrintVect-test-page.xps 192.168.1.5 "Counter 1 Laser"
pvct-send status 192.168.1.5 <job id>
```

`192.168.1.5` is the host PC's address (shown on its Share my printers page, and under Network in Settings, Diagnostics); a PC
name works too. `PrintVect-test-page.xps` is a ready-made one-page test file. Any other
`.xps` file works: print a document to "Microsoft XPS Document Writer" to make one. Options:
`/pin 1234` when the host has a PIN, `/doc "title"`, `/port 9151`, `/nowait`.

If another PC cannot reach the host, the Windows Firewall on the host is blocking port 9151 (on a phone
hotspot: see Troubleshooting). Without the installer, run this once on the host as administrator:

```
netsh advfirewall firewall add rule name="PrintVect Jobs (TCP-In)" dir=in action=allow protocol=TCP localport=9151 program="C:\PrintVect\PrintVect.App.exe" profile=private,domain
netsh advfirewall firewall add rule name="PrintVect Discovery (UDP-In)" dir=in action=allow protocol=UDP localport=9150 program="C:\PrintVect\PrintVect.App.exe" profile=private,domain
```

## Where things are

| Item | Location |
|------|----------|
| Settings | `C:\ProgramData\PrintVect\config.json` |
| Job files in transit | `C:\ProgramData\PrintVect\spool\` (received files: `spool\incoming\`, deleted once printed) |
| Logs (one per day, kept 14 days) | `C:\ProgramData\PrintVect\logs\PrintVect-<date>.log` |
| Admin helper log | `C:\ProgramData\PrintVect\logs\PrintVect-Elevate-<date>.log` |
| pvct-send log | `C:\ProgramData\PrintVect\logs\PrintVect-Send-<date>.log` |
| Downloaded update and its setup log | `C:\ProgramData\PrintVect\updates\PrintVect-Setup-<version>.exe`, `update-<version>.log` |
| Ports | UDP 9150 (finding printers), TCP 9151 (sending jobs) |
| Update check | HTTPS to `api.github.com` and `github.com` only (this repository's releases) |

`ProgramData` is a hidden folder; type the path into the Explorer address bar.

## Troubleshooting

| Problem | What to do |
|---------|------------|
| "PrintVect could not create its data folder" at start | Ask whoever installed PrintVect to check that `C:\ProgramData\PrintVect` can be written to. |
| No tray icon after starting | Check the hidden icons arrow next to the clock. If still nothing, look for the newest file in `C:\ProgramData\PrintVect\logs\`. |
| Chrome or Edge blocks the download as dangerous | Unsigned new program; use **Download dangerous file** / **Keep** from the download's menu (see above). |
| Antivirus on Windows 7 quarantines the EXE | Add the PrintVect folder and `C:\ProgramData\PrintVect` to its exclusions; the program is unsigned for now. |
| "is reachable but nothing is listening" from pvct-send | On the host, open PrintVect and switch sharing ON. |
| "Neither Microsoft XPS Document Writer nor ... v4 is installed" when adding a printer | PrintVect installs Windows' own XPS Document Writer itself. If it still fails: Settings, Apps, Optional features, More Windows features, tick **Microsoft XPS Document Writer**, OK, restart if asked, then Add again. |
| "is not reachable on the network" from another PC | Check the host's IP address on its Share my printers page, then add the firewall rule above on the host. |
| Printing stops when the PCs use a phone hotspot | Windows treats a hotspot as a **public** network, and PrintVect's firewall rules let other PCs in on private networks only. On the PC that shares the printer, Share my printers shows a yellow note: press **Make it private** and answer **Yes** (or in Windows: Settings, Network, the Wi-Fi, Network profile type **Private**). A PC that prints finds the host's new hotspot address by itself; waiting jobs: **Retry waiting** on Office printers. |
| The host says the job is still printing after 60 s | Look at the printer and at the Windows print queue on the host (Settings, Printers & scanners, the printer, Open print queue). The job is in that queue and prints when the printer is ready; PrintVect keeps reporting its page progress for up to 15 minutes. |
| Microsoft Print to PDF: the job shows Printing until you answer the Save window | That is how Windows works: choose a file name and the job becomes Printed. |
| Settings says "Could not check for a newer version: this PC cannot reach github.com" | The PC has no internet connection right now (or a proxy blocks github.com). Printing is not affected; the check tries again the next day, or press **Check now** later. |
| "The update did not run (the setup program ended with code ...)" | The Windows permission question was answered No, or the setup failed: the reason is in `C:\ProgramData\PrintVect\updates\update-<version>.log`. Press **Update now** again. |
| After an update PrintVect did not come back | Start it from the Start menu; the update itself is complete. Tell the person who supports PrintVect which version the window shows. |
| A job waits with "asks for the office PIN" | The two PCs have different office PINs (or one has none). Set the same PIN in Settings on both, then press **Retry waiting** (Office printers) on the PC that printed. |
| A job waits with "is paused there" | Someone pressed **Pause** on the PC that has the printer. Press **Resume** there (Share my printers). |
| A job waits with "stopped answering after it received this job" | The PC with the printer was switched off or lost the network during the job. When it is back, check whether the page came out; if not, press **Retry waiting** (or right-click the job, **Retry**). A page that already printed is never printed twice. |
| Update now says jobs are still being handled | A shared printer is paused with jobs waiting. Resume it (or wait until they printed), then press Update now again. |
| Something else | Settings, Diagnostics, **Copy report**, and send the text to the person who supports PrintVect. |

## Publishing a new version

PCs that run PrintVect look at this repository's **latest release** once a day and offer it with one
click, so an update reaches the office by making a release. First the version number must go up:
raise the three version lines in `Directory.Build.props` (for example `0.2.0` to `0.2.1`), commit,
push, and wait for the green build. Then, on the GitHub website:

1. Open `https://github.com/rahulranjan-dev-py/PrintVect/releases/new`.
2. Click **Choose a tag**, type `v0.2.1`, then click **Create new tag: v0.2.1 on publish**.
3. Click **Target: main** and pick the branch that has the new version. Do not skip this.
4. In **Release title** type `PrintVect 0.2.1`. Leave everything else as it is.
5. Click **Publish release**.
6. Wait five minutes and refresh: the release now lists `PrintVect-Setup-0.2.1.exe` and
   `PrintVect-Setup-0.2.1.exe.sha256`, built and attached by GitHub (Actions, run named **release**).

Until the files are there, the PCs report "release v0.2.1 has no PrintVect-Setup file attached" and
try again later. Drafts and pre-releases are ignored by the PCs. A release whose version already has
a setup program is left alone (the run only warns), so the version number must always go up first.
For people with git, pushing a tag (`git tag v0.2.1 && git push origin v0.2.1`) does the same, and so
does **Actions**, **release**, **Run workflow** once the workflow file is on the default branch.

On each PC, PrintVect then shows a balloon and a yellow strip "A newer PrintVect (0.2.1) is ready to
install". **Update now** downloads the setup program into `C:\ProgramData\PrintVect\updates\`, checks
its size and SHA-256, and runs it silently; Windows asks the usual permission question once, PrintVect
closes for a few seconds and opens again as the new version. Nothing is installed without that click.

## For developers

- `CLAUDE.md` holds the condensed rules; the full brief is in `docs/`.
- `docs/protocol.md` describes the wire protocol; `docs/testing.md` the manual test steps per milestone.
- Every user-visible string lives in `src/PrintVect.App/Strings.resx`. After editing it without
  Visual Studio, run `python3 tools/generate_strings.py`.
- Licence: MIT (see `LICENSE`).
