#define AppName "King of Music"
#define AppVersion "0.1.0"
#define AppPublisher "Universidad"
#define AppExeName "KingOfMusic.exe"

[Setup]
AppId={{6ED1E348-1D72-4539-917C-9307B511D46F}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\KingOfMusic
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
UsePreviousAppDir=yes
OutputDir=..\Installers
OutputBaseFilename=KingOfMusic_Setup
SetupIconFile=kingofmusic.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
WizardStyle=modern
WizardImageFile=wizard_image.bmp
WizardSmallImageFile=wizard_small.bmp
WizardImageStretch=no
Compression=lzma2/fast
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=yes
RestartApplications=no
DisableWelcomePage=no
DisableReadyPage=no
DisableFinishedPage=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; GroupDescription: "Accesos directos:"; Flags: checkedonce

[Files]
Source: "..\Builds\KingOfMusic\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "KingOfMusic_BackUpThisFolder_ButDontShipItWithYourGame\*"

[Icons]
Name: "{group}\King of Music"; Filename: "{app}\{#AppExeName}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\Desinstalar King of Music"; Filename: "{uninstallexe}"
Name: "{autodesktop}\King of Music"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon; IconFilename: "{app}\{#AppExeName}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Abrir King of Music"; Flags: nowait postinstall skipifsilent

[Code]
// Unity deja al lado del ejecutable una carpeta "BackUpThisFolder_ButDontShipItWithYourGame"
// con las DLL de referencia. Los [Files] la excluyen, pero recursesubdirs crea la carpeta vacia:
// quitarla deja la instalacion limpia.
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    DelTree(ExpandConstant('{app}\KingOfMusic_BackUpThisFolder_ButDontShipItWithYourGame'), True, True, True);
end;
