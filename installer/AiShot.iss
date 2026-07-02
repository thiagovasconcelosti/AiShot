; Script Inno Setup do AiShot — instalador por usuário (sem admin).
; Compilar: "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\AiShot.iss
; Requer a pasta publicada em dist\app (self-contained NAO single-file — menos
; falso-positivo de antivirus que exe empacotado/auto-extraivel).

#define AppName "AiShot"
#define AppVersion "0.1.1"
#define AppPublisher "Thiago Vasconcelos"
#define AppExe "AiShot.exe"

[Setup]
AppId={{9A0C2C1E-4E2B-4B7A-9A1F-AISHOT000001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=AiShot-Setup-{#AppVersion}
SetupIconFile=..\src\AiShot\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; GroupDescription: "Atalhos:"
Name: "startup"; Description: "Iniciar o AiShot com o Windows"; GroupDescription: "Inicialização:"

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "AiShot"; ValueData: """{app}\{#AppExe}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "Iniciar o {#AppName} agora"; Flags: nowait postinstall skipifsilent
