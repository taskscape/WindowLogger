; Installer script for Window Logger
; Requires: Inno Setup 6.x (https://jrsoftware.org/)

#define MyAppName "Window Logger"
#define MyAppFolderName "WindowLogger"
#define MyAppVersion "1.0"
#define MyAppPublisher "Taskscape"
#define MyAppExeName "WindowLoggerTray.exe"

[Setup]
; Unique AppId (generated for this specific application)
AppId={{E6593582-1594-4177-8332-972134599723}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; Install directly to "Program Files\WindowLogger"
DefaultDirName={autopf}\{#MyAppFolderName}
DefaultGroupName={#MyAppFolderName}
; Require admin privileges (installing to Program Files)
PrivilegesRequired=admin
; Output filename
OutputBaseFilename=WindowLoggerInstaller
Compression=lzma
SolidCompression=yes
WizardStyle=modern
; 64-bit architecture (important for .NET)
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start Window Logger automatically with Windows (Elevated/Admin)"; GroupDescription: "Startup options";

[Files]
; NOTE: Relative paths assume the .iss file is located next to the .sln file

; 1. Tray Application (Controller)
Source: "WindowLoggerTray\bin\Release\net10.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; 2. Window Logger (Background Service)
Source: "WindowLogger\bin\Release\net10.0\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; 3. Analyser (Reports + Config)
; This contains 'appsettings.json', which will be the default configuration
Source: "WindowAnalyser\bin\Release\net10.0\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; 4. Config GUI (Editor)
Source: "WindowLoggerConfigGui\bin\Release\net48\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; Start Menu shortcut
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
; Desktop shortcut
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 1. Create a Scheduled Task to run the app as Admin on logon (Only if 'startup' task is selected)
;    /SC LOGON = Trigger at logon
;    /RL HIGHEST = Run with highest privileges (Admin)
;    /F = Force create (overwrite if exists)
Filename: "schtasks"; \
    Parameters: "/Create /F /TN ""{#MyAppName} Autostart"" /TR ""'""{app}\{#MyAppExeName}""'"" /SC LOGON /RL HIGHEST"; \
    Flags: runhidden; \
    Tasks: startup

; 2. Launch the app immediately after installation
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; Remove the Scheduled Task when uninstalling
Filename: "schtasks"; Parameters: "/Delete /TN ""{#MyAppName} Autostart"" /F"; Flags: runhidden
