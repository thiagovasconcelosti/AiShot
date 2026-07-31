# Documentação técnica

## Stack

- **.NET 10**, **C#**, **WinForms** (`net10.0-windows`), apenas Windows.
- Sem dependências de runtime de terceiros — só a BCL (`System.Net.Http`, `System.Text.Json`, `System.Drawing`, WinForms).
- Ícones renderizados a partir da fonte **Phosphor** embutida.

## Arquitetura

```
Program.Main [STAThread]
 └─ TrayAppContext (ícone da bandeja, menu, atalho global, limpeza de temp)
      ├─ AppConfig (JSON + DPAPI + overrides por env)
      ├─ GlobalHotKey (hook low-level WH_KEYBOARD_LL)
      ├─ StartupManager (chave Run em HKCU)
      ├─ SettingsForm (UI de configuração)
      └─ AppHost : ICaptureServices
            ├─ AiService : IAiService  (visão → principal → fallback)
            │    ├─ AiProviderFactory → IAiProvider (Anthropic | OpenAi)
            │    └─ IAiChatSession (chat contínuo)
            └─ ImageUploaderFactory → IImageUploader (FreeImage | Imgbb)

CaptureOverlay (Form) — seleção + edição in-place, consome ICaptureServices
 ├─ SelectionGeometry  (geometria pura: hit-test, resize/move, clamp)
 ├─ ShapeRenderer      (desenho das anotações)
 ├─ ToolbarLayout      (layout dos botões, ciente do monitor)
 └─ ChatPanel          (chat da IA: timeline, scroll, sessão)

UI/Theme, UI/Icons — helpers de desenho (paleta shadcn-dark, fonte Phosphor)
```

As camadas de domínio (`Ai/`, `Imaging/`) não referenciam WinForms — operam sobre `byte[]`/`HttpClient` atrás de interfaces, então são testáveis. A UI conversa com elas via `ICaptureServices`.

## Configuração

A config fica em `%APPDATA%\AiShot\appsettings.json`. Formato:

```json
{
  "HotKey": "PrintScreen",
  "Ai": {
    "Provider": "openai",
    "ApiKey": "enc:...",
    "Model": "deepseek-v4-flash",
    "BaseUrl": "https://api.deepseek.com",
    "Fallback": { "Provider": "openai", "ApiKey": "enc:...", "Model": "...", "BaseUrl": "..." },
    "Vision":   { "Enabled": true, "Provider": "openai", "ApiKey": "enc:...", "Model": "...", "BaseUrl": "..." }
  },
  "ImageUpload": { "Service": "freeimage", "ApiKey": "" }
}
```

### Segurança — armazenamento de credenciais
As chaves de API são cifradas em repouso com **DPAPI** (`ProtectedData`, `DataProtectionScope.CurrentUser`, com prefixo `enc:`). Um arquivo copiado para outra máquina/usuário não pode ser decifrado. Os saves são atômicos (`.tmp` + move). Um `appsettings.json` legado ao lado do executável é migrado para o local cifrado em `%APPDATA%` no primeiro carregamento.

### Overrides por ambiente
Cada configuração pode ser sobrescrita por uma env var com prefixo `AISHOT_` (maior precedência, nunca gravada em disco):

| Var | Efeito |
|-----|--------|
| `AISHOT_AI__PROVIDER` | `anthropic` ou `openai` |
| `AISHOT_AI__APIKEY` | chave da IA principal |
| `AISHOT_AI__MODEL` / `AISHOT_AI__BASEURL` | modelo / base URL principal |
| `AISHOT_AI__FALLBACK__APIKEY` | chave do fallback |
| `AISHOT_AI__VISION__ENABLED` | `true`/`false` |
| `AISHOT_AI__VISION__APIKEY` / `__MODEL` | chave / modelo da visão |
| `AISHOT_IMAGEUPLOAD__SERVICE` / `__APIKEY` | host de imagem / chave |
| `AISHOT_HOTKEY` | ex.: `PrintScreen`, `Ctrl+Alt+S` |

## Pipeline de IA

`AiService.AskAboutImageAsync` / `IAiChatSession`:

1. **Visão (opcional):** se ativa, um modelo de visão descreve a imagem **uma vez** (cache na sessão).
2. **Principal:** a descrição é injetada no system prompt; o provedor principal responde. Com visão ativa, os bytes da imagem não são reenviados ao modelo principal.
3. **Fallback:** qualquer exceção do provedor principal aciona o fallback (se configurado).

Os provedores implementam `IAiProvider` (`AnthropicProvider`, `OpenAiProvider`) e são clientes REST OpenAI/Anthropic sobre um `HttpClient` compartilhado. Corpos de erro HTTP são truncados antes de exibir.

### Streaming

`IAiProvider.StreamAsync` devolve a resposta em incrementos por Server-Sent Events (`ServerSentEvents` lê o fluxo; cada provedor fornece o extrator do seu formato). O chat desenha o texto conforme ele chega, em vez de esperar a resposta inteira.

Se o provedor principal falhar **no meio do fluxo**, o texto parcial já exibido é descartado antes de o fallback recomeçar — o callback recebe string vazia para sinalizar isso. Emendar o começo de uma resposta no fim de outra produziria um texto que nenhum dos dois modelos escreveu.

## Atalho global

`GlobalHotKey` usa um **hook de teclado low-level** (`WH_KEYBOARD_LL`) em vez de `RegisterHotKey`, porque no Windows 11 o `PrintScreen` é reservado pela Ferramenta de Captura e o `RegisterHotKey` falha/é roubado. O hook intercepta a tecla antes e a **suprime**. Um **modo de captura** deixa a janela de Configurações ler a combinação pressionada sem disparar uma captura.

## Build e publish

```sh
# Build de debug
dotnet build src/AiShot/AiShot.csproj -c Debug

# Self-contained, arquivo único comprimido (~49 MB, sem precisar do .NET)
dotnet publish src/AiShot/AiShot.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none

# Framework-dependent (~0,75 MB, exige o .NET 10 Desktop Runtime)
dotnet publish src/AiShot/AiShot.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

> Trimming/AOT **não** são suportados em WinForms (reflection). Os 111 MB do self-contained sem compressão são o runtime do .NET, não o app.

## Instalador e empacotamento

- **Instalador:** script Inno Setup em `installer/AiShot.iss` (por usuário, sem admin). Compile com `ISCC.exe`.
- **Chocolatey:** pacote em `chocolatey/` — `chocolateyinstall.ps1` baixa o instalador do release e verifica o **SHA256**.

## Estrutura do projeto

```
src/AiShot/
  Program.cs, App/ (TrayAppContext, AppHost, StartupManager)
  Capture/ (CaptureOverlay, ChatPanel, SelectionGeometry, ShapeRenderer, ToolbarLayout, Annotation)
  Ai/ (IAiProvider, AiService, AiProviderFactory, Providers/, ServerSentEvents, HttpUtil)
  Imaging/ (IImageUploader, FreeImageUploader, ImgbbUploader, ImageUploaderFactory)
  Config/ (AppConfig, SecretProtector)
  HotKey/ (GlobalHotKey)
  Settings/ (SettingsForm)
  UI/ (Theme, Icons), Assets/ (Phosphor.ttf, app.ico)
installer/ (AiShot.iss, aishot.png)
chocolatey/ (aishot.nuspec, tools/)
docs/ (este site)
```
