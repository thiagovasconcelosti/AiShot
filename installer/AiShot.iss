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
    if MsgBox('Remover tambem o historico de capturas guardado em disco?'#13#10#13#10
              + 'Sao imagens do que estava na sua tela. Se voce escolher Nao, elas'#13#10
              + 'permanecem em:'#13#10 + Historico,
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      RemoverPasta(Historico);
  end;

  if DirExists(Config) then
  begin
    if MsgBox('Remover tambem as configuracoes e as chaves de API salvas?'#13#10#13#10
              + 'Escolha Nao para manter suas configuracoes caso pretenda'#13#10
              + 'reinstalar o AiShot.',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      RemoverPasta(Config);
  end;

  { A pasta-mae em LOCALAPPDATA so sai se ficou vazia — pode conter o historico
    que o usuario acabou de escolher manter. }
  RemoveDir(ExpandConstant('{localappdata}\AiShot'));
end;
