# Publishing a new version

PCs that run PrintVect look at this repository's **latest release** once a day and offer it with one
click, so an update reaches every PC by making a release. GitHub's "latest release" is never a
**pre-release**, and that is how a new version is tried first (0.5.8): it is published as a **test
version** (a pre-release) that only the PCs you install it on get, and handed to every PC once it works.

## 1. Get the version ready

1. Raise the three version lines in `Directory.Build.props` (for example `0.2.0` to `0.2.1`).
2. Write what changed under a heading `## 0.2.1` at the top of `CHANGELOG.md`, ending with
   `**Tested:** awaiting test.` This text becomes the release text, so write it for the people who use PrintVect.
3. Commit, push, and wait for the green build (a pull request merged into `main`).

## 2. Publish it as a test version

On GitHub: **Actions**, **release**, **Run workflow**, branch `main`, stage **test**, **Run workflow**.
Five minutes later the release `v0.2.1` is there, marked **Pre-release**, with `PrintVect-Setup-0.2.1.exe`,
`PrintVect-Setup-0.2.1.exe.sha256` and the text from `CHANGELOG.md` under a "Test version" note.

PCs that run PrintVect do **not** take it by themselves. On each PC you test on, download
`PrintVect-Setup-0.2.1.exe` from that release and run it: it updates PrintVect in place and keeps the
settings and added printers. Follow the steps for that version in `docs/testing.md`.

## 3. Hand it to every PC

When the test PCs work: change `**Tested:** awaiting test.` in `CHANGELOG.md` to what was tested (for example
`**Tested:** Windows 10 and 11, 2026-11-02.`) and get that onto `main`. Then **Actions**, **promote**,
**Run workflow**, branch `main` (leave the version empty for the one in `Directory.Build.props`, or type it).
The release loses its Pre-release mark, becomes the latest release, and its text is written again from
`CHANGELOG.md` without the test note. The same can be done on the release page (Edit, untick
"Set as a pre-release", tick "Set as the latest release", Update release), but then the text stays as it was.

On each PC, PrintVect then shows a balloon and a yellow strip "A newer PrintVect (0.2.1) is ready to
install" within a day. **Update now** downloads the setup program into `C:\ProgramData\PrintVect\updates\`,
checks its size and SHA-256 (a release without its `.sha256` is not installed, 0.5.8), checks the file again
just before it starts, and runs it silently; Windows asks the usual permission question once, PrintVect
closes for a few seconds and opens again as the new version. Nothing is installed without that click.

## Other ways, and what to know

- **Straight to every PC** (an urgent fix): **Run workflow** with stage **everyone**. Use it rarely: the version
  then reaches every PC within a day untried.
- **Pushing a tag** (`git tag v0.2.1 && git push origin v0.2.1`) makes a test version, as Run workflow does.
- **On the GitHub website**: Releases, **Draft a new release**, **Choose a tag** `v0.2.1` (**Create new tag on
  publish**), **Target** = the branch with the new version, title `PrintVect 0.2.1`, tick **Set as a
  pre-release** for a test version, **Publish release**. The setup program and its checksum are attached a few
  minutes later (Actions, run named **release**), and the text from `CHANGELOG.md` when none was typed. Until the
  files are there, the PCs report "release v0.2.1 has no PrintVect-Setup file attached" and try again later.
- A release whose version already has a setup program is left alone (the run only warns), so the version number
  must always go up first. Drafts and pre-releases are ignored by the PCs.
- The release text comes from `tools/release_notes.py`: it fails the run when `CHANGELOG.md` has no section for
  the version, so nothing is published without saying what changed.
