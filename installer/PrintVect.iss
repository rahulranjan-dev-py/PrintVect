; PrintVect installer (Inno Setup 6, milestone M5).
; CI builds it with:  ISCC.exe /DAppVersion=0.1.0 /DSourceDir=<folder with the built program files> installer\PrintVect.iss
; The source folder is what the build workflow collects: PrintVect.App.exe, PrintVect.Core.dll,
; Newtonsoft.Json.dll, PrintVect.Elevate.exe, pvct-send.exe, Fonts\, the sample .xps files, README.md, LICENSE.

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\PrintVect"
#endif
#define AppName "PrintVect"
#define AppExe "PrintVect.App.exe"
#define AppUrl "https://github.com/rahulranjan-dev-py/PrintVect"
#define FwTcpRule "PrintVect Jobs (TCP-In)"
#define FwUdpRule "PrintVect Discovery (UDP-In)"

[Setup]
AppId={{A7D4C1E2-6B3F-4A58-9E0D-2F1C7B8A9D31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesInstallIn64BitMode=x64
MinVersion=6.1sp1
PrivilegesRequired=admin
LicenseFile=..\LICENSE
SetupIconFile=..\src\PrintVect.App\PrintVect.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=PrintVect-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ShowLanguageDialog=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Start {#AppName} with Windows (it sits in the tray)"; GroupDescription: "Start-up:"
Name: "firewall"; Description: "Open {#AppName}'s two ports in Windows Firewall (needed when this PC shares a printer; harmless otherwise)"; GroupDescription: "Network:"
Name: "desktopicon"; Description: "Put a {#AppName} icon on the desktop"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Dirs]
; Every user of the PC may write here: the spooler drops print files into spool\<id>, and the
; logs and job history are shared (CLAUDE.md: files under ProgramData belong to whoever made them).
Name: "{commonappdata}\{#AppName}"; Permissions: users-modify
Name: "{commonappdata}\{#AppName}\spool"; Permissions: users-modify
Name: "{commonappdata}\{#AppName}\logs"; Permissions: users-modify

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{group}\{#AppName} read me"; Filename: "{app}\README.md"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; Autostart for every user, hidden in the tray (the app's /tray switch).
Root: HKLM; Subkey: "SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"" /tray"; Flags: uninsdeletevalue; Tasks: autostart

[Run]
; Firewall rules, program-scoped, private and domain networks only (brief, section 10). Delete first so a re-install never doubles them.
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FwTcpRule}"""; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#FwTcpRule}"" dir=in action=allow protocol=TCP localport=9151 program=""{app}\{#AppExe}"" profile=private,domain"; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FwUdpRule}"""; Flags: runhidden; Tasks: firewall
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""{#FwUdpRule}"" dir=in action=allow protocol=UDP localport=9150 program=""{app}\{#AppExe}"" profile=private,domain"; Flags: runhidden; Tasks: firewall
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName} now"; Flags: postinstall nowait skipifsilent

[UninstallRun]
; Order matters: printers and ports first (while the helper still exists), then the firewall rules.
Filename: "{app}\PrintVect.Elevate.exe"; Parameters: "remove-all"; Flags: runhidden waituntilterminated; RunOnceId: "RemovePrinters"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FwTcpRule}"""; Flags: runhidden; RunOnceId: "FirewallTcp"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""{#FwUdpRule}"""; Flags: runhidden; RunOnceId: "FirewallUdp"

[UninstallDelete]
; A clean uninstall leaves no folders behind (brief, section 10): settings, spool files, logs and job history go too.
Type: filesandordirs; Name: "{commonappdata}\{#AppName}"

[Code]
const
  DotNet48Release = 528040;
  OldManualCopy = 'C:\PrintVect\PrintVect.App.exe';

function DotNet48Present: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release)
            and (Release >= DotNet48Release);
end;

procedure StopPrintVect;
var
  ResultCode: Integer;
begin
  { The tray app has no window to close, so ask Windows to end it; the installer replaces its files next. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM PrintVect.App.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM pvct-send.exe /F /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function InitializeSetup: Boolean;
begin
  Result := DotNet48Present;
  if not Result then
    MsgBox('PrintVect needs the .NET Framework 4.8.' + #13#10#13#10
           + 'Windows 10 (version 1903 or later) and Windows 11 already have it. On an older Windows, install '
           + '".NET Framework 4.8 offline installer" (ndp48-x86-x64-allos-enu.exe) from Microsoft first, then run this setup again.',
           mbError, MB_OK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopPrintVect;
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and FileExists(OldManualCopy) then
    MsgBox('An older copy of PrintVect from the test builds is still in C:\PrintVect.' + #13#10#13#10
           + 'The installed program now lives in ' + ExpandConstant('{app}') + ' and uses the same settings, so you can delete the C:\PrintVect folder.',
           mbInformation, MB_OK);
end;

function InitializeUninstall: Boolean;
begin
  StopPrintVect;
  Result := True;
end;
