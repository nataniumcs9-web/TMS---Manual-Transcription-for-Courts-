#define AppName "GZHC Court Transcriber"
#define AppVersion "1.0.3"
#define AppPublisher "Gedeo Zone High Court"
#define AppExeName "TranscriberClient.exe"
#define PublishDir "..\TranscriberClient\publish\installer-payload"

[Setup]
AppId={{B60A3FE8-3F93-4FE3-A1B3-CE5D2F1B24D7}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\GZHC Court Transcriber
DefaultGroupName={#AppName}
DisableProgramGroupPage=no
DisableDirPage=no
DisableReadyPage=no
DisableFinishedPage=no
AlwaysShowDirOnReadyPage=yes
AlwaysShowGroupOnReadyPage=yes
AllowNoIcons=yes
PrivilegesRequired=lowest
Uninstallable=yes
OutputDir=output
OutputBaseFilename=GZHC-Transcriber-Setup-{#AppVersion}
SetupIconFile=..\TranscriberClient\Assets\gzhc.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
VersionInfoVersion=1.0.3.0
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
SetupLogging=yes
CloseApplications=yes
RestartApplications=no

[Tasks]
Name: "startmenuicon"; Description: "Create a Start Menu shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: startmenuicon
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: postinstall nowait skipifsilent
