# Contributing

Thank you for helping. PrintVect is used by people who are not IT staff, so the most useful contributions are often
not code:

- **Problems:** open an [issue](https://github.com/rahulranjan-dev-py/PrintVect/issues/new/choose) with the PrintVect
  version, the Windows version and the Diagnostics report (Settings, Diagnostics, Copy report).
- **Ideas:** open an issue that describes your situation, at home or at work, and what gets in the way.
- **Words:** if a message in PrintVect is hard to understand, say so in an issue and suggest better words.

## Code

- C# 7.3 on .NET Framework 4.8, WinForms. Build and test as in the README ("Building from source"); CI builds and
  tests every pull request on Windows.
- No new NuGet packages in the shipped program (Newtonsoft.Json is the only one); please ask in an issue first.
- Every user-visible text lives in `src/PrintVect.App/Strings.resx`; after editing it without Visual Studio, run
  `python3 tools/generate_strings.py`.
- Printing and finding printers stay on the local network. The update check is the only internet connection, and
  that stays so.
- Every error is logged with its job id and shown as a plain message that says what to do next.
- `CLAUDE.md` lists the project's rules in full; `docs/protocol.md` describes the network protocol.

Open a pull request against `main`. Small, focused pull requests are easiest to review.
