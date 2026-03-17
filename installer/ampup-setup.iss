; Inno Setup Script for Amp Up
; Download Inno Setup from https://jrsoftware.org/isinfo.php

#define MyAppName "Amp Up"
#include "version.iss"
#define MyAppPublisher "Tyson Wolf"
#define MyAppExeName "AmpUp.exe"
#define MyAppURL "https://github.com/audioslayer/ampup"

[Setup]
AppId={{E7B3F2A1-9C4D-4E8F-B6A2-1D3F5E7A9B0C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\installer\output
OutputBaseFilename=AmpUp-Setup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\ampup.ico
SetupIconFile=..\Assets\ampup.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "startupentry"; Description: "Start Amp Up with Windows"; GroupDescription: "Startup:"

[Files]
; Include all published files from the self-contained publish output
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Include icon file for shortcuts
Source: "..\Assets\ampup.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\ampup.ico"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\ampup.ico"; Tasks: desktopicon

[Registry]
; Optional startup entry
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "AmpUp"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startupentry

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  DOTNET_RUNTIME_URL = 'https://download.visualstudio.microsoft.com/download/pr/dotnet-runtime-8-desktop-win-x64.exe';
  DOTNET_MIN_VERSION = '8.0';

function IsDotNet8Installed(): Boolean;
var
  ResultCode: Integer;
begin
  // Check if dotnet runtime 8.x is available
  Result := Exec('dotnet', '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  if Result then
  begin
    // Verify Microsoft.WindowsDesktop.App 8.x is present
    Result := RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App\8.0');
    if not Result then
      Result := RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App\8.0');
    if not Result then
    begin
      // Fallback: check via registry for any 8.x WindowsDesktop runtime
      if RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') then
      begin
        // Key exists, check if any 8.x subkey
        Result := False; // Conservative: let installer offer to download
      end;
    end;
  end;
end;

function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  // Kill running instances before install/upgrade
  Exec('taskkill', '/f /im "AmpUp.exe"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    if not IsDotNet8Installed() then
    begin
      if MsgBox('Amp Up requires the .NET 8 Desktop Runtime which is not installed.' + #13#10 + #13#10 +
                'Click YES to open the download page (one-time ~55MB install from Microsoft).' + #13#10 +
                'Click NO to skip (Amp Up may not launch without it).',
                mbConfirmation, MB_YESNO) = IDYES then
      begin
        ShellExec('open', 'https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.14-windows-x64-installer', '', '', SW_SHOW, ewNoWait, ResultCode);
        MsgBox('After installing .NET 8 Desktop Runtime, you can launch Amp Up.', mbInformation, MB_OK);
      end;
    end;
  end;
end;
