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
- The setup program and the program files are not code-signed yet (see [CODE_SIGNING.md](CODE_SIGNING.md)); the
  updater checks each downloaded setup program's size and SHA-256 against the release before running it.
