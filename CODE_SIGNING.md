# Code signing and privacy

**Status: PrintVect is not signed yet.** The setup program and the program files carry no publisher signature, so
Windows SmartScreen may warn before the setup program runs, and a Windows 11 PC with Smart App Control On refuses to
run it. Signing will come with a code signing certificate; this page will then say from which version on releases are
signed.

## When releases are signed

These rules will apply from the first signed release:

- The setup program published on this repository's
  [GitHub Releases](https://github.com/rahulranjan-dev-py/PrintVect/releases), `PrintVect-Setup-<version>.exe`, and
  the PrintVect program files inside it: `PrintVect.App.exe`, `PrintVect.Core.dll`, `PrintVect.Elevate.exe` and
  `pvct-send.exe`. Files made by others (for example `Newtonsoft.Json.dll`) are shipped as their authors publish them.
- Only files that GitHub Actions builds from the source code in this public repository are signed, by the release
  workflow [`.github/workflows/release.yml`](.github/workflows/release.yml). Nothing built on a personal computer is
  signed.
- Every signing request is approved by hand by an approver (below) before the release is published.

## Team roles

- **Committers and reviewers:** Rahul Ranjan ([rahulranjan-dev-py](https://github.com/rahulranjan-dev-py)). Changes are
  prepared with Claude Code, an AI coding assistant, on a separate branch, built and unit-tested by GitHub Actions,
  tried on the office PCs, and merged into `main` through pull requests by the maintainer.
- **Approvers:** Rahul Ranjan ([rahulranjan-dev-py](https://github.com/rahulranjan-dev-py)).

## Privacy policy

PrintVect collects no personal data and sends no documents, usage statistics or crash reports to its authors or to
anyone else.

- **Inside the local network.** PCs running PrintVect find each other (UDP port 9150) and send print jobs (TCP port
  9151) to the PC that has the printer. Every 5 minutes a PC that prints on another PC's printers tells that PC its
  name, its Windows version, its PrintVect version and the printers it uses, so that PC can show "PCs that print here".
  None of this leaves the local network.
- **The internet.** Once a day, and when someone presses *Check now*, PrintVect asks GitHub (`api.github.com`) for the
  latest release of this repository; when someone presses *Update now* it downloads the new setup program from GitHub.
  GitHub then sees the PC's public IP address and the program name and version (`PrintVect/<version>`); see
  [GitHub's privacy statement](https://docs.github.com/site-policy/privacy-policies/github-general-privacy-statement).
  The daily check can be switched off in Settings, Updates. PrintVect makes no other internet connection.
- **On the PC.** Settings, job history and logs stay in `C:\ProgramData\PrintVect` on that PC. The Diagnostics report
  leaves the PC only when a person presses *Copy report* and pastes it somewhere.

## Third-party components

All open source: [Newtonsoft.Json](https://www.newtonsoft.com/json) (MIT licence); the Noto Sans and JetBrains Mono
fonts (SIL Open Font License 1.1, see `src/PrintVect.App/Fonts`); the setup program is made with
[Inno Setup](https://jrsoftware.org/isinfo.php) (Inno Setup License). PrintVect itself is under the MIT licence
([LICENSE](LICENSE)).

## Problems

Please open an [issue](https://github.com/rahulranjan-dev-py/PrintVect/issues) on GitHub.
