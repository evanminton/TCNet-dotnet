; Inno Setup script for TCNet Monitor (MAUI app + tcnet-monitor CLI), Windows x64.
; Built by installer\build-installer.ps1, which passes /DAppVersion /DAppDir /DCliDir /DOutDir [/DAppIcon].

#define AppName "TCNet Monitor"
#define AppExe "TCNet.Maui.exe"
#define CliExe "tcnet-monitor.exe"
#ifndef AppVersion
  #define AppVersion "2.0.0"
#endif
#ifndef AppDir
  #define AppDir "..\artifacts\publish\app"
#endif
#ifndef CliDir
  #define CliDir "..\artifacts\publish\cli"
#endif
#ifndef OutDir
  #define OutDir "..\artifacts\installer"
#endif

[Setup]
AppId={{74836DB5-3540-4DAD-B6B0-ADF49B1CB2FD}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Evan Minton
AppPublisherURL=https://github.com/evanminton/TCNet-dotnet
AppSupportURL=https://github.com/evanminton/TCNet-dotnet
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Installs per user without admin; choose "all users" in the dialog to also add firewall rules.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
LicenseFile=..\LICENSE
OutputDir={#OutDir}
OutputBaseFilename=TCNet-Monitor-Setup-{#AppVersion}-win-x64
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ChangesEnvironment=yes
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName} {#AppVersion}
CloseApplications=yes
#ifdef AppIcon
SetupIconFile={#AppIcon}
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked
Name: "addtopath"; Description: "Add the tcnet-monitor command-line tool to PATH"; GroupDescription: "Command-line tool:"
Name: "firewall"; Description: "Allow TCNet Monitor and the CLI through Windows Firewall (UDP)"; GroupDescription: "Network:"; Check: IsAdminInstallMode

[Files]
Source: "{#AppDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#CliDir}\{#CliExe}"; DestDir: "{app}\cli"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autoprograms}\TCNet Monitor CLI"; Filename: "{cmd}"; Parameters: "/k tcnet-monitor --help"; WorkingDir: "{app}\cli"; IconFilename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""TCNet Monitor"" dir=in action=allow program=""{app}\{#AppExe}"" protocol=UDP enable=yes"; Flags: runhidden; Tasks: firewall; StatusMsg: "Adding firewall rule..."
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""TCNet Monitor CLI"" dir=in action=allow program=""{app}\cli\{#CliExe}"" protocol=UDP enable=yes"; Flags: runhidden; Tasks: firewall
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""TCNet Monitor"""; Flags: runhidden; RunOnceId: "fw-app"; Check: IsAdminInstallMode
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""TCNet Monitor CLI"""; Flags: runhidden; RunOnceId: "fw-cli"; Check: IsAdminInstallMode

[Code]
function EnvRoot: Integer;
begin
  if IsAdminInstallMode then Result := HKEY_LOCAL_MACHINE else Result := HKEY_CURRENT_USER;
end;

function EnvKey: String;
begin
  if IsAdminInstallMode then
    Result := 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment'
  else
    Result := 'Environment';
end;

function CliDirPath: String;
begin
  Result := ExpandConstant('{app}\cli');
end;

procedure AddToPath;
var P: String;
begin
  if not RegQueryStringValue(EnvRoot, EnvKey, 'Path', P) then P := '';
  if Pos(';' + Uppercase(CliDirPath) + ';', ';' + Uppercase(P) + ';') > 0 then Exit;
  if (P <> '') and (Copy(P, Length(P), 1) <> ';') then P := P + ';';
  RegWriteExpandStringValue(EnvRoot, EnvKey, 'Path', P + CliDirPath);
end;

procedure RemoveFromPath;
var P, U, D: String; I: Integer;
begin
  if not RegQueryStringValue(EnvRoot, EnvKey, 'Path', P) then Exit;
  P := ';' + P + ';';
  U := Uppercase(P);
  D := ';' + Uppercase(CliDirPath) + ';';
  I := Pos(D, U);
  if I = 0 then Exit;
  Delete(P, I, Length(D) - 1);
  P := Copy(P, 2, Length(P) - 2);
  RegWriteExpandStringValue(EnvRoot, EnvKey, 'Path', P);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and WizardIsTaskSelected('addtopath') then AddToPath;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then RemoveFromPath;
end;
