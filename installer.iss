; Instalador do ko-phonetic (Inno Setup 6) - https://jrsoftware.org/isinfo.php
[Setup]
AppId={{B6F2D3A1-4C7E-4E59-9A18-3D5E7C2F9A64}
AppName=ko-phonetic
AppVersion=1.0
DefaultDirName={autopf}\KoPhonetic
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputBaseFilename=KoPhonetic-Setup
UninstallDisplayIcon={app}\KoPhonetic.exe
UninstallDisplayName=ko-phonetic
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "autostart"; Description: "Iniciar com o Windows (arranca desligado)"; Flags: unchecked

[Files]
Source: "bin\Release\net462\KoPhonetic.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\ko-phonetic"; Filename: "{app}\KoPhonetic.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "KoPhoneticWin"; ValueData: """{app}\KoPhonetic.exe"" --auto"; Tasks: autostart

[Run]
Filename: "{app}\KoPhonetic.exe"; Description: "Abrir o ko-phonetic"; Flags: nowait postinstall skipifsilent

[Code]
procedure KillApp;
var
  R: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM KoPhonetic.exe', '', SW_HIDE, ewWaitUntilTerminated, R);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillApp;
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    KillApp;
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'KoPhoneticWin');
end;
