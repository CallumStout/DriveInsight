#define MyAppId "{{07746BE0-D25B-4E87-BF61-06BB2A80B5EF}}"
#define MyAppName "DriveInsight"
#define MyAppVersion "0.4.2"
#define CompanyName "97 Solutions"
#define SetupName "DriveInsightSetup"
#define AppIcon (SourcePath + "Artwork\")
#define PathToBinary (SourcePath + "..\DriveInsight\bin\Release\net10.0")
#define MyAppExeName "DriveInsight.exe"

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={commonpf64}\{#CompanyName}\{#MyAppName}
DefaultGroupName={#CompanyName}\{#MyAppName}
OutputDir=..\Release\97Solutions
OutputBaseFilename={#SetupName}
Compression=lzma
SolidCompression=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
PrivilegesRequired=admin
SetupIconFile="{#AppIcon}App.ico"
UninstallDisplayIcon={app}\{#MyAppExeName}
WizardStyle=modern
WizardImageFile="{#AppIcon}InnoLarge.bmp"
WizardSmallImageFile="{#AppIcon}InnoSmall.bmp"
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PathToBinary}\\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Flags: nowait postinstall skipifsilent
