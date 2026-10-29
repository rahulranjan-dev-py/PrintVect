#!/usr/bin/env python3
"""Writes the text of a GitHub release from CHANGELOG.md (used by .github/workflows/release.yml and promote.yml).

A test version (pre-release) says so at the top: PCs that run PrintVect do not install it by themselves, because
GitHub's "latest release", which they ask for, never is a pre-release. promote.yml rewrites the text without that
note when the version is handed to every PC.

    python3 tools/release_notes.py 0.5.8 --test --out release-notes.md
"""
import argparse
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
REPO = "https://github.com/rahulranjan-dev-py/PrintVect"


def changes_for(version, changelog):
    """The text under "## <version>" (with or without a date after it), up to the next "## " heading."""
    pattern = r"^## " + re.escape(version) + r"(?![0-9.])[^\n]*\n(.*?)(?=^## |\Z)"
    match = re.search(pattern, changelog, re.MULTILINE | re.DOTALL)
    return match.group(1).strip() if match else None


def unwrap(text):
    """Joins the lines of each plain paragraph: GitHub shows a line break in release text as a break."""
    blocks = []
    for block in re.split(r"\n\s*\n", text):
        lines = block.split("\n")
        plain = not any(re.match(r"\s*([-*+|#>]|[0-9]+\.)\s", line) for line in lines)
        blocks.append(" ".join(line.strip() for line in lines) if plain else block)
    return "\n\n".join(blocks)


def notes(version, test, changelog):
    changes = changes_for(version, changelog)
    if changes is not None:
        changes = unwrap(changes)
    if changes is None:
        sys.exit("CHANGELOG.md has no section \"## %s\": write what changed there first." % version)
    parts = []
    if test:
        parts.append("**Test version.** PCs that run PrintVect do not install it by themselves. To try it, download "
                     "**PrintVect-Setup-%s.exe** below and run it on the PCs you test on. Once it works, it is handed to "
                     "every PC with Actions, **promote**, Run workflow (docs/releasing.md)." % version)
    parts.append("## What changed in %s\n\n%s" % (version, changes))
    parts.append("PrintVect shares a printer with every Windows PC and laptop on your network, at home or in the office, "
                 "with no printer driver to install and no Windows password on the PCs that print. "
                 "What it does and how to use it: %s#readme" % REPO)
    if not test:
        parts.append("Download **PrintVect-Setup-%s.exe** and run it on a PC that does not have PrintVect yet. "
                     "PCs that already run PrintVect find this version by themselves within a day (Settings tab, Updates) "
                     "and install it with one click." % version)
    parts.append("The .sha256 file is the checksum PrintVect verifies after downloading. All versions and what each "
                 "changed: %s/blob/main/CHANGELOG.md" % REPO)
    return "\n\n".join(parts) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("version", help="for example 0.5.8")
    parser.add_argument("--test", action="store_true", help="a test version (pre-release)")
    parser.add_argument("--out", default="release-notes.md")
    args = parser.parse_args()
    if not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", args.version):
        sys.exit("The version must look like 0.5.8, not \"%s\"." % args.version)
    with open(os.path.join(ROOT, "CHANGELOG.md"), encoding="utf-8") as f:
        changelog = f.read().replace("\r\n", "\n")
    with open(args.out, "w", encoding="utf-8", newline="\n") as f:
        f.write(notes(args.version, args.test, changelog))
    print("wrote", args.out)


if __name__ == "__main__":
    main()
