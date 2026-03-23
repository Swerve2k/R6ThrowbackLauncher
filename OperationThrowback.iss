; ============================================================
;  OPERATION THROWBACK - Inno Setup Installer Script
;  Place this .iss file in the ROOT of your project folder
; ============================================================

#define AppName      "Operation Throwback"
#define AppVersion   "0.2.0"
#define AppPublisher "Cele D. Luffy"
#define AppExeName   "R6ThrowbackLauncher.exe"
#define AppURL       "https://github.com/CeleDLuffy/R6ThrowbackLauncher"
#define BuildOutput  "C:\Users\Omari\Downloads\R6ThrowbackLauncher_v0.2.0\R6ThrowbackLauncher\bin\Release\net8.0-windows\publish"

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
WizardImageBackColor=$140D0D
PrivilegesRequired=admin
MinVersion=10.0
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
WelcomeLabel2=This will install [name/ver] on your computer.%n%nOperation Throwback lets you download and play legacy Rainbow Six Siege seasons.%n%nClick Next to continue.

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce

[Files]
Source: "{#BuildOutput}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "C:\Users\Omari\Downloads\R6ThrowbackLauncher_v0.2.0\R6ThrowbackLauncher\Resources\*"; DestDir: "{app}\Resources"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\OperationThrowback"
