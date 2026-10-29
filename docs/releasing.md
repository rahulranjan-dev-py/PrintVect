# Publishing a new version

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
its size and SHA-256 (a release without its `.sha256` is not installed, 0.5.8), checks the file again just before it
starts, and runs it silently; Windows asks the usual permission question once, PrintVect
closes for a few seconds and opens again as the new version. Nothing is installed without that click.
