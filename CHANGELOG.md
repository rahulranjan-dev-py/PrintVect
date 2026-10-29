# Changes

Newest first. Each version is a [release](https://github.com/rahulranjan-dev-py/PrintVect/releases) with its setup
program; PCs that run PrintVect offer the newest one by themselves (Settings, Updates). **Tested** says how far the
version was tried on the office's PCs (Windows 10 and 11).

## 0.5.8

Security fixes from a review of the design.

The sharing PIN is harder to steal and to guess. It never crosses the network any more, not even as the plain
SHA-256 hash earlier versions sent, which anyone watching the network could reuse as the password forever. Each job
carries a one-time proof of the PIN instead: it is made for that job only and works for nothing else. A PC that sends
5 wrong PINs has to wait 15 minutes before the PC with the printer checks its PIN again, twice as long each further
time (at most 4 hours). With a PIN, the list of PCs that print here only shows PCs that know the PIN. **Update every
PC**: with a sharing PIN, a PC on 0.5.7 and a PC on 0.5.8 cannot print to each other.

Another PC can no longer take over a printer by answering with the same PC name when the real one has moved or is
switched off. Each PC that shares printers now has its own secret identity key, and every PC that adds one of its
printers remembers it (printers added before learn it within a few minutes). A printer follows its PC to a new address
only when the PC there proves it holds that key; otherwise nothing is sent and the job says why.

The administrator helper can no longer be tricked through the shared data folder: every user may write in
`C:\ProgramData\PrintVect`, so another user could put a link (junction) there that leads to a Windows folder and wait
for the helper to give "Users: Modify" rights or delete files through it. The helper now refuses to create, change or
delete anything through such a link and says what to remove. **Tested:** awaiting test.

## 0.5.7 (2026-10-29)

The copies window: it opens about 2 to 3 seconds sooner after a program prints (the PC now looks at the finished
print file within half a second instead of waiting for its 2-second check), it is as tall as its content so the
buttons no longer cover the text above them, the switch reads "Don't ask again on this PC" on one line with "Turn it
back on in Settings, General." under it, and the button just says **Print**: the minute until it prints by itself is
a line under the buttons, so it is not mistaken for the number of copies. **Tested:** awaiting test.

## 0.5.6 (2026-10-28)

The menu fits "Printers on other PCs" in full, also in bold as the open page: it now measures the page names on the
PC and makes itself wide enough (at least 248 px). **Tested:** awaiting test.

## 0.5.5 (2026-10-27)

No office words in the app, because PrintVect serves homes too: the Office printers page is now **Printers on other
PCs**, the office PIN the **sharing PIN**, "Found in the office" is "Found on the network", and "Share with the
office" is "Share with other PCs". The menu is 16 px wider so its longest name fits. Settings, the PIN and added
printers stay as they are. **Tested:** awaiting test.

## 0.5.4 (2026-10-27)

When Windows blocks an update (Smart App Control, or a rule that allows only approved programs), a plain message says
so and what to do; Diagnostics shows whether Smart App Control is on. **Tested:** awaiting test.

## 0.5.3 (2026-10-24)

Layout fixes: pages use the width of a maximised window (centred beyond 1400 px), printer rows on Share my printers as
tall as their text, dark scroll bar on the job list, visible switch knob in Dark colours, the settings path no longer
broken mid-word, a failed update check tried again after 30 minutes. **Tested:** awaiting test.

## 0.5.2 (2026-10-23)

The window no longer opens from the tray as empty white boxes; pages switch at once. **Tested:** awaiting test.

## 0.5.1 (2026-10-23)

Pages open in one go: no more jumping or flicker when moving between Office printers, Share my printers, Jobs and
Settings. **Tested:** awaiting test.

## 0.5.0 (2026-10-22)

The redesign: side menu, Light / Dark / System default colours, Office printers page, Turn on sharing window,
PrintVect's own printers can no longer be shared again; the phone hotspot fix (Make it private, a job finds a host's
new address by itself). **Tested:** awaiting test.

## 0.4.1 (2026-10-21)

Jobs page: right-click Retry / Cancel job / Remove from list / Copy details, Cancel all waiting, Clear list; bigger
green tick boxes on Share my printers. **Tested:** awaiting test.

## 0.4.0 (2026-10-17)

Hardening (milestone M6b-2): editable settings and office PIN, copies question, PCs that use a host, Pause / Resume,
jobs kept for Retry when a host is lost, plain-language errors. **Tested:** not tested.

## 0.3.0 and earlier (2026-09-29 to 2026-10-17)

Milestones M0 to M6b-1 (see `docs/brief.md`, section 9; MudranSetu was the working name
before PrintVect was chosen):

| Milestone | What it delivers | Tested |
|-----------|------------------|--------|
| M0 | Solution scaffold, CI build, empty tray app with Diagnostics tab | verified on Windows 11 |
| M1 | Host role: printer list, share toggle, job listener, printing, `pvct-send` test tool | verified on a Windows 11 host with a Windows 10 sender |
| M2 | Client role: virtual printer through the Elevate helper, spool watcher, status balloons, Use tab | verified on Windows 10 and 11 clients |
| M3 | Hosts found by themselves (UDP discovery on every network card), found-printers list, job history that survives a restart | verified on two Windows 11 PCs and a Windows 10 PC |
| M4 | Windows 7 pass | skipped: no Windows 7 PC in the office; tested on Windows 10 and 11 only |
| M5 | Installer (Inno Setup): Program Files, firewall rules, autostart, clean uninstall, bundled fonts and the new icon | verified on Windows 10 (daily use) |
| M6a | Updates from inside the program: daily look at the GitHub releases, one-click install, release workflow | verified on three PCs (0.2.0 to 0.2.1 by itself) |
| M6b-1 | The new look (docs/design): dark Graphite window, bundled fonts, top bar with pages, Jobs page, Diagnostics inside Settings | reported OK by the owner |
