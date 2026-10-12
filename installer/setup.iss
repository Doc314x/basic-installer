; Inno Setup Skript fuer Basic Software Installer
; AppVersion muss manuell synchron zu <Version> in
; src\BasicInstaller\BasicInstaller.csproj gepflegt werden - kein automatischer Abgleich.

#define MyAppName "Basic Software Installer"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "AS Computertechnik"
#define MyAppPublisherURL "https://as-ct.de"
#define MyAppExeName "BasicInstaller.exe"

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppPublisherURL}
DefaultDirName={autopf}\BasicInstaller
DefaultGroupName={#MyAppName}
PrivilegesRequired=admin
OutputDir=Output
OutputBaseFilename=BasicInstaller-Setup
SetupIconFile=..\assets\app.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Kompletter Release-Build-Output (inkl. Unterordner wie catalog\) wird uebernommen.
Source: "..\src\BasicInstaller\bin\Release\net48\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
