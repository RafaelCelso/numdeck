; NumDeck installer (Inno Setup 6). Built by scripts\build-release.ps1:
;   ISCC.exe /DAppVersion=1.0.0 /DSourceDir=..\publish /DOutputDir=..\dist installer\NumDeck.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "NumDeck"
#define AppExe "NumDeck.exe"
#define AppUrl "https://github.com/RafaelCelso/numdeck"

[Setup]
; Fixed id: lets newer versions upgrade the installed one in place. Never change it.
AppId={{6F3D2A91-4C7B-4E1D-9A58-2B7C0E6F1D42}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Rafael Celso
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}
; Per-user install by default (no admin prompt); user may choose "all users".
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=NumDeck-Setup-{#AppVersion}
SetupIconFile=..\src\NumDeck.App\Assets\numdeck.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; A running NumDeck is closed by the [Code] section below (it listens for the
; "NumDeck.Exit" event), so install/upgrade/uninstall also work silently.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
ptbr.StartWithWindows=Iniciar o NumDeck junto com o Windows
en.StartWithWindows=Start NumDeck with Windows
ptbr.ExtraOptions=Opções:
en.ExtraOptions=Options:

[Tasks]
Name: "autostart"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:ExtraOptions}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
; The hook DLL may be loaded inside other running programs; if it's locked,
; it is replaced on the next restart instead of failing the upgrade.
Source: "{#SourceDir}\native\NumDeckHook64.dll"; DestDir: "{app}\native"; Flags: ignoreversion restartreplace uninsrestartdelete

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value the app's own "Iniciar com o Windows" checkbox manages.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "NumDeck"; \
  ValueData: """{app}\{#AppExe}"" --minimized"; Tasks: autostart; Flags: uninsdeletevalue
; Always clean the autostart entry on uninstall, even if it was enabled later from the app.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "NumDeck"; \
  Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

; Settings in %APPDATA%\NumDeck are kept on uninstall, so reinstalling restores them.

[Code]
const
  EVENT_MODIFY_STATE = $0002;
  SYNCHRONIZE = $00100000;

function OpenEvent(Access: Cardinal; Inherit: Boolean; Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Handle: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function OpenMutex(Access: Cardinal; Inherit: Boolean; Name: String): THandle;
  external 'OpenMutexW@kernel32.dll stdcall';

function NumDeckRunning(): Boolean;
var
  H: THandle;
begin
  H := OpenMutex(SYNCHRONIZE, False, 'NumDeck.SingleInstance');
  Result := H <> 0;
  if Result then CloseHandle(H);
end;

{ Asks NumDeck to quit (saves settings, removes its keyboard hook); kills it if it doesn't. }
procedure CloseNumDeck();
var
  H: THandle;
  I, Code: Integer;
begin
  if not NumDeckRunning() then Exit;
  H := OpenEvent(EVENT_MODIFY_STATE, False, 'NumDeck.Exit');
  if H <> 0 then
  begin
    SetEvent(H);
    CloseHandle(H);
  end;
  for I := 1 to 30 do
  begin
    if not NumDeckRunning() then Break;
    Sleep(100);
  end;
  if NumDeckRunning() then
  begin
    Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, Code);
    Sleep(300);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseNumDeck();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  CloseNumDeck();
  Result := True;
end;
