; Inno Setup script. Build with:  iscc /DAppVersion=2.0.0 installer\IslamicNashat.iss  (release.ps1 does this)
; Expects the published app in publish\ (IslamicNashat.exe plus its Assets folder).

#ifndef AppVersion
  #define AppVersion "2.0.0"
#endif

[Setup]
AppId={{B7E2C1A4-5D3F-4E8B-9A61-2F4C8D7E3B15}
AppName=Islamic Nashat
AppVersion={#AppVersion}
AppPublisher=Nashat Shamsadyn
AppPublisherURL=https://iosbb0.web.app
DefaultDirName={localappdata}\Programs\IslamicNashat
DisableProgramGroupPage=yes
; Per-user install: no admin prompt.
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=IslamicNashat-Setup-{#AppVersion}
SetupIconFile=..\src\IslamicNashat\Assets\icon.ico
UninstallDisplayIcon={app}\IslamicNashat.exe
UninstallDisplayName=Islamic Nashat
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start with Windows"; GroupDescription: "Startup:"

[Files]
Source: "..\publish\IslamicNashat.exe"; DestDir: "{app}"; Flags: ignoreversion
; SQLite and the media player need these as real files next to the exe.
Source: "..\publish\Assets\*"; DestDir: "{app}\Assets"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\Islamic Nashat"; Filename: "{app}\IslamicNashat.exe"
Name: "{autodesktop}\Islamic Nashat"; Filename: "{app}\IslamicNashat.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "IslamicNashat"; \
  ValueData: """{app}\IslamicNashat.exe"" --startup"; Flags: uninsdeletevalue; Tasks: startup

[InstallDelete]
; The 1.x Python widget started itself from this script; two widgets would call every adhan twice.
Type: files; Name: "{userstartup}\IslamicNashatWidget.vbs"

[Run]
Filename: "{app}\IslamicNashat.exe"; Description: "{cm:LaunchProgram,Islamic Nashat}"; Flags: nowait postinstall skipifsilent
; The in-app updater runs this installer with /SILENT /RELAUNCH and expects the app to come back.
Filename: "{app}\IslamicNashat.exe"; Flags: nowait; Check: IsRelaunch

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/im IslamicNashat.exe /f"; Flags: runhidden; RunOnceId: "StopIslamicNashat"

[Code]
function IsRelaunch: Boolean;
begin
  Result := Pos('/RELAUNCH', UpperCase(GetCmdTail)) > 0;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  // Stop the 1.x widget if it is running, so it can't announce alongside the new one.
  Exec('taskkill.exe', '/im IslamicNashatWidget.exe /f', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;
