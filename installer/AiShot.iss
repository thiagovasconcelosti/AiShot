; Script Inno Setup do AiShot — instalador por usuário (sem admin).
; Compilar: "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" installer\AiShot.iss
; Requer a pasta publicada em dist\app (self-contained NAO single-file — menos
; falso-positivo de antivirus que exe empacotado/auto-extraivel).

#define AppName "AiShot"
; A versão vem da linha de comando do compilador (ISCC /DAppVersion=x.y.z), que
; a lê de Directory.Build.props — a fonte única do repositório. O valor abaixo
; é apenas o padrão para compilações locais feitas sem o parâmetro.
#ifndef AppVersion
  #define AppVersion "0.1.3"
#endif
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
; Auto-update: fecha o app em execução para sobrescrever os arquivos.
CloseApplications=yes
RestartApplications=no

; O idioma do instalador segue o do Windows; o seletor so aparece quando o
; sistema esta num idioma que nao esta nesta lista. O ingles vem primeiro por
; ser o padrao do Inno quando nada corresponde.
[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

; Textos proprios do instalador, por idioma. Sem prefixo de idioma valeriam
; para todos — que era o caso antes, com tudo em portugues.
[CustomMessages]
english.DesktopIcon=Create a desktop shortcut
english.Shortcuts=Shortcuts:
english.RunAtStartup=Start AiShot with Windows
english.Startup=Startup:
english.UninstallShortcut=Uninstall AiShot
english.LaunchNow=Launch AiShot now
english.RemoveHistory=Also remove the capture history stored on disk?%n%nThese are images of what was on your screen. If you choose No, they remain in:%n%1
english.RemoveConfig=Also remove the saved settings and API keys?%n%nChoose No to keep your settings if you plan to reinstall AiShot.

brazilianportuguese.DesktopIcon=Criar atalho na área de trabalho
brazilianportuguese.Shortcuts=Atalhos:
brazilianportuguese.RunAtStartup=Iniciar o AiShot com o Windows
brazilianportuguese.Startup=Inicialização:
brazilianportuguese.UninstallShortcut=Desinstalar AiShot
brazilianportuguese.LaunchNow=Iniciar o AiShot agora
brazilianportuguese.RemoveHistory=Remover também o histórico de capturas guardado em disco?%n%nSão imagens do que estava na sua tela. Se você escolher Não, elas permanecem em:%n%1
brazilianportuguese.RemoveConfig=Remover também as configurações e as chaves de API salvas?%n%nEscolha Não para manter suas configurações caso pretenda reinstalar o AiShot.

spanish.DesktopIcon=Crear acceso directo en el escritorio
spanish.Shortcuts=Accesos directos:
spanish.RunAtStartup=Iniciar AiShot con Windows
spanish.Startup=Inicio:
spanish.UninstallShortcut=Desinstalar AiShot
spanish.LaunchNow=Iniciar AiShot ahora
spanish.RemoveHistory=¿Eliminar también el historial de capturas guardado en disco?%n%nSon imágenes de lo que había en tu pantalla. Si eliges No, permanecen en:%n%1
spanish.RemoveConfig=¿Eliminar también la configuración y las claves de API guardadas?%n%nElige No para conservar tu configuración si piensas reinstalar AiShot.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopIcon}"; GroupDescription: "{cm:Shortcuts}"
Name: "startup"; Description: "{cm:RunAtStartup}"; GroupDescription: "{cm:Startup}"

[Files]
Source: "..\dist\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Licenca propria e avisos dos componentes de terceiros. O WebView2 (BSD
; 3-Clause) exige que a redistribuicao binaria reproduza o aviso de copyright
; na documentacao que acompanha o programa; Phosphor e os pacotes da interface
; sao MIT, que exige o mesmo.
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "AiShot"; ValueData: """{app}\{#AppExe}"""; Tasks: startup; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall skipifsilent

; Caches e perfis recriados na proxima execucao: saem sempre, sem perguntar.
; A configuracao e o historico ficam de fora daqui — sao dados do usuario, e a
; remocao deles e decidida em CurUninstallStepChanged.
[UninstallDelete]
Type: filesandordirs; Name: "{%TEMP}\AiShot.WebView2"
Type: filesandordirs; Name: "{%TEMP}\AiShot.webui"
Type: files; Name: "{%TEMP}\aishot_*.png"

[Code]
{ Remove uma pasta inteira, ignorando o que nao existe. }
procedure RemoverPasta(const Caminho: string);
begin
  if DirExists(Caminho) then
    DelTree(Caminho, True, True, True);
end;

{ Pergunta sobre os dados do usuario depois de desinstalar o programa.

  Sao duas perguntas separadas de proposito. A configuracao guarda as chaves de
  API (cifradas por DPAPI, inuteis em outra maquina) e quem reinstala costuma
  querer manter. O historico guarda imagens do que estava na tela, que e o dado
  mais sensivel que o app produz — quem desinstala e nao apaga precisa saber que
  as imagens continuam la. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Config, Historico: string;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  Config := ExpandConstant('{userappdata}\AiShot');
  Historico := ExpandConstant('{localappdata}\AiShot\history');

  { Silencioso (/VERYSILENT): sem ninguem para responder, os dados ficam.
    Apagar por conta propria seria pior do que deixar. }
  if UninstallSilent then
    Exit;

  if DirExists(Historico) then
  begin
    if MsgBox(FmtMessage(CustomMessage('RemoveHistory'), [Historico]),
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      RemoverPasta(Historico);
  end;

  if DirExists(Config) then
  begin
    if MsgBox(CustomMessage('RemoveConfig'),
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      RemoverPasta(Config);
  end;

  { A pasta-mae em LOCALAPPDATA so sai se ficou vazia — pode conter o historico
    que o usuario acabou de escolher manter. }
  RemoveDir(ExpandConstant('{localappdata}\AiShot'));
end;
