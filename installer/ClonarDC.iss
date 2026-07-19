#define MyAppName "Clonar DC"
#define MyAppVersion "0.5.0"
#define MyAppPublisher "Clonar DC"
#define MyAppExeName "ClonarDC.exe"

[Setup]
AppId={{D9D864B5-6B0B-44F9-9C61-6E3D12DF8A17}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\Clonar DC
DefaultGroupName=Clonar DC
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\out\installer
OutputBaseFilename=Clonar-DC-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\ClonarDC.Desktop\Assets\ClonarDC.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName=Clonar DC
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=yes
ShowLanguageDialog=yes
LanguageDetectionMethod=none
UsePreviousLanguage=yes

[Languages]
Name: "en-US"; MessagesFile: "compiler:Default.isl"
Name: "pt-BR"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "es-ES"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "fr-FR"; MessagesFile: "compiler:Languages\French.isl"
Name: "de-DE"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
en-US.DesktopShortcut=Create a desktop shortcut
en-US.ShortcutsGroup=Shortcuts:
en-US.OpenApp=Open Clonar DC
pt-BR.DesktopShortcut=Criar atalho na Área de Trabalho
pt-BR.ShortcutsGroup=Atalhos:
pt-BR.OpenApp=Abrir Clonar DC
es-ES.DesktopShortcut=Crear un acceso directo en el escritorio
es-ES.ShortcutsGroup=Accesos directos:
es-ES.OpenApp=Abrir Clonar DC
fr-FR.DesktopShortcut=Créer un raccourci sur le Bureau
fr-FR.ShortcutsGroup=Raccourcis :
fr-FR.OpenApp=Ouvrir Clonar DC
de-DE.DesktopShortcut=Desktop-Verknüpfung erstellen
de-DE.ShortcutsGroup=Verknüpfungen:
de-DE.OpenApp=Clonar DC öffnen

[Files]
Source: "..\out\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\out\backend\*"; DestDir: "{app}\backend"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKCU; Subkey: "Software\Clonar DC"; ValueType: string; ValueName: "Language"; ValueData: "{language}"

[Icons]
Name: "{autoprograms}\Clonar DC"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\Clonar DC"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:ShortcutsGroup}"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:OpenApp}"; Flags: nowait postinstall skipifsilent