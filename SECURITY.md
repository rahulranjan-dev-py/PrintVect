# Security

## Reporting a security problem

Please do not describe a security problem in a public issue. Instead, use **Report a vulnerability** on this
repository's [Security tab](https://github.com/rahulranjan-dev-py/PrintVect/security), which only the maintainer can
read; the answer comes there too.

## Supported versions

Only the [latest release](https://github.com/rahulranjan-dev-py/PrintVect/releases/latest) gets fixes. Every installed
PrintVect offers the newest version by itself (Settings, Updates), so a fix reaches every PC with one click.

## How PrintVect is meant to be used

These are known limits of the design, not vulnerabilities:

- PrintVect is meant for a trusted home or office network. Its ports (UDP 9150, TCP 9151) are opened in Windows Firewall on
  private and domain networks only.
- Without a sharing PIN, any PC on the same network can print to the shared printers. With a PIN (0.5.8), every job and
  hello carries a one-time proof of it (PBKDF2-SHA256 salted with that request), never the PIN or a plain hash of it,
  and a PC that sends 5 wrong PINs must wait 15 minutes, twice as long each further time. Someone who records the
  network traffic can still try to guess a short PIN offline, so use 8 or more characters where that matters. Print
  jobs are not encrypted on the network.
- A PC name and printer ids can be claimed by any PC on the network. Since 0.5.8 a printer follows its host to a new
  address only when the PC there proves, by signing a random challenge with the host's identity key, that it is the
  PC the printer was added from. Someone who can intercept the network traffic itself (not just answer on it) is
  outside what PrintVect can stop.
- Every user may write in `C:\ProgramData\PrintVect`. The administrator helper (`PrintVect.Elevate.exe`) checks that no
  folder it creates, gives rights on, deletes or writes its log and result in is a junction or symbolic link, and
  gives the spool folder its rights through a handle to the folder it checked (0.5.8).
- The setup program and the program files are not code-signed yet (see [CODE_SIGNING.md](CODE_SIGNING.md)); the
  updater checks each downloaded setup program's size and SHA-256 against the release before running it. The checksum
  file comes from the same GitHub release, so it catches a damaged download, not a release replaced by someone who
  took over the repository. Since 0.5.8 a release without a checksum file is not installed, and the setup program is
  held open (nobody can change, delete or rename it or its folder) and checked again just before it starts.
