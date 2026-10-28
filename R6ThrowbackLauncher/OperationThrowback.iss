; Operation Throwback installer (Inno Setup 6)
; Build the app first:  dotnet publish -c Release
; Paths are relative to this script so it compiles on any machine.

#define AppName      "Operation Throwback"
#define AppVersion   "3.2.2"
#define AppPublisher "Swerve2k"
#define AppExeName   "R6ThrowbackLauncher.exe"
#define AppURL       "https://github.com/Swerve2k/R6ThrowbackLauncher"
#define BuildOutput  SourcePath + "bin\Release\net8.0-windows\publish"

[Setup]
AppId={{A3F2B1C4-9D7E-4F8A-B2C3-1E5D6A7B8C9D}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} v{#AppVersion}
AppPublisherURL={#AppURL}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\OperationThrowback
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=installer_output
OutputBaseFilename=OperationThrowback_Setup_v{#AppVersion}
SetupIconFile=Resources\icon.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSizePercent=120
WizardImageFile=installer_assets\banner.bmp
WizardSmallImageFile=installer_assets\logo_small.bmp
WizardImageStretch=no
WizardImageBackColor=$0A0A0A
PrivilegesRequired=admin
MinVersion=10.0
CloseApplications=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
VersionInfoVersion={#AppVersion}
VersionInfoDescription={#AppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nOperation Throwback downloads, manages and launches older seasons of Rainbow Six Siege.%n%nClick Next to continue.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
Source: "{#BuildOutput}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "Resources\*"; DestDir: "{app}\Resources"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Run]
; No skipifsilent: the in-app updater runs this installer with /SILENT and relies on it to restart the app.
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName} now"; Flags: nowait postinstall

[UninstallDelete]
; Only helper caches are removed. Downloaded seasons, settings and playtime in
; %LOCALAPPDATA%\OperationThrowback are kept so reinstalling doesn't cost a redownload.
Type: filesandordirs; Name: "{localappdata}\OperationThrowback\bin"
Type: filesandordirs; Name: "{localappdata}\OperationThrowback\Prerequisites"
Type: files; Name: "{localappdata}\OperationThrowback\crash.log"
