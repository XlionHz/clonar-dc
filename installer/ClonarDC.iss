#define MyAppName "GuildSync"
#define MyAppVersion "0.8.1"
#define MyAppPublisher "GuildSync"
#define MyAppExeName "ClonarDC.exe"

[Setup]
AppId={{D9D864B5-6B0B-44F9-9C61-6E3D12DF8A17}
AppName={#MyAppName}
AppVerName={#MyAppName} {#MyAppVersion}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion=0.8.1.0
DefaultDirName={localappdata}\Programs\GuildSync
DefaultGroupName=GuildSync
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\out\installer
OutputBaseFilename=GuildSync-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src\ClonarDC.Desktop\Assets\GuildSync.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName=GuildSync
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=yes
SetupLogging=yes
UsePreviousAppDir=yes
ShowLanguageDialog=yes
LanguageDetectionMethod=none
UsePreviousLanguage=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[CustomMessages]
en.DesktopShortcut=Create a desktop shortcut
en.ShortcutsGroup=Shortcuts:
en.OpenApp=Open GuildSync
ptbr.DesktopShortcut=Criar atalho na Área de Trabalho
ptbr.ShortcutsGroup=Atalhos:
ptbr.OpenApp=Abrir GuildSync
es.DesktopShortcut=Crear un acceso directo en el escritorio
es.ShortcutsGroup=Accesos directos:
es.OpenApp=Abrir GuildSync
fr.DesktopShortcut=Créer un raccourci sur le Bureau
fr.ShortcutsGroup=Raccourcis :
fr.OpenApp=Ouvrir GuildSync
de.DesktopShortcut=Desktop-Verknüpfung erstellen
de.ShortcutsGroup=Verknüpfungen:
de.OpenApp=GuildSync öffnen

[Files]
Source: "..\out\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\out\backend\*"; DestDir: "{app}\backend"; Flags: ignoreversion recursesubdirs createallsubdirs

[Registry]
Root: HKCU; Subkey: "Software\Clonar DC"; ValueType: string; ValueName: "Language"; ValueData: "{code:GetAppCulture}"
Root: HKCU; Subkey: "Software\GuildSync"; ValueType: string; ValueName: "Language"; ValueData: "{code:GetAppCulture}"
Root: HKCU; Subkey: "Software\GuildSync"; ValueType: string; ValueName: "ApiUrl"; ValueData: "https://clonar-dc-api.onrender.com"

[Icons]
Name: "{autoprograms}\GuildSync"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\GuildSync"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopShortcut}"; GroupDescription: "{cm:ShortcutsGroup}"; Flags: unchecked

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:OpenApp}"; Flags: nowait postinstall skipifsilent

[Code]
function GetAppCulture(Param: string): string;
var
  Selected: string;
begin
  Selected := ActiveLanguage;
  if Selected = 'ptbr' then
    Result := 'pt-BR'
  else if Selected = 'es' then
    Result := 'es-ES'
  else if Selected = 'fr' then
    Result := 'fr-FR'
  else if Selected = 'de' then
    Result := 'de-DE'
  else
    Result := 'en-US';
end;
