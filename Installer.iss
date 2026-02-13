; Installer script for Window Logger
; Requires: Inno Setup 6.x (https://jrsoftware.org/)

#define MyAppName "Window Logger"
#define MyAppVersion "1.0"
#define MyAppPublisher "Taskscape"
#define MyAppExeName "WindowLoggerTray.exe"

[Setup]
AppId={{E6593582-1594-4177-8332-972134599723}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppPublisher}\{#MyAppName}
DefaultGroupName={#MyAppName}
; Require admin to install
PrivilegesRequired=admin
OutputBaseFilename=WindowLoggerInstaller
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
; NOTE: We changed the description to reflect that it uses Task Scheduler for Admin rights
Name: "startup"; Description: "Start Window Logger automatically with Windows (Elevated/Admin)"; GroupDescription: "Startup options";

[Files]
Source: "WindowLoggerTray\bin\Release\net10.0-windows\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "WindowLogger\bin\Release\net10.0\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "WindowAnalyser\bin\Release\net10.0\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "WindowLoggerConfigGui\bin\Release\net48\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

; [Registry] <-- REMOVED! We don't use Registry for Admin autostart.

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
