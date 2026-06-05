#define MyAppName "RoleAxis Desktop"
#define MyAppVersion "1.0.0"
#define MyAppExeName "RoleAxis.Desktop.exe"

[Setup]
AppId={{7E92D760-1E6B-4F36-9CE5-4F7A90A10002}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=RoleAxis
AppPublisherURL=http://127.0.0.1:8000
DefaultDirName={autopf}\RoleAxis\RoleAxis Desktop
DefaultGroupName={#MyAppName}
OutputDir=installer-output
OutputBaseFilename=RoleAxis-Desktop-Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Files]
Source: "bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilentcb   
