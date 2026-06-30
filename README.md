# AiShot

Clone do Lightshot/prntscr voltado para IA. Captura de tela, anotação (setas, formas, cores), copiar / salvar / upload para serviço de imagem grátis, e um botão **Perguntar à IA** sobre o print.

- **.NET 10 / C# / WinForms** — Windows only.
- Fica no **system tray**. Atalho global padrão: **PrintScreen**.
- Duplo-clique no ícone ou menu → captura; menu → **Configurações**.

## Funcionalidades

- Captura de região multi-monitor (overlay escurecido estilo Lightshot).
- Editor: seta, retângulo, elipse, linha, lápis, texto, cor e espessura, Undo (Ctrl+Z).
- **Copiar** (clipboard), **Salvar** (PNG/JPG), **Upload** (freeimage.host / imgbb), **Perguntar à IA**.
- IA: provider `anthropic` ou `openai`, **fallback** automático e **IA de visão opcional**.
  Quando a visão está ativa, ela descreve a imagem **antes** da IA principal responder.

## Configuração

Edite `appsettings.json` (ao lado do executável) ou use **Configurações** no tray.
Tudo pode ser sobrescrito por variáveis de ambiente com prefixo `AISHOT_`:

| Var | Efeito |
|-----|--------|
| `AISHOT_AI__PROVIDER` | `anthropic` ou `openai` |
| `AISHOT_AI__APIKEY` | chave da IA principal |
| `AISHOT_AI__MODEL` | modelo principal |
| `AISHOT_AI__FALLBACK__APIKEY` | chave do fallback |
| `AISHOT_AI__VISION__ENABLED` | `true`/`false` |
| `AISHOT_AI__VISION__APIKEY` | chave da IA de visão |
| `AISHOT_IMAGEUPLOAD__SERVICE` | `freeimage` ou `imgbb` |
| `AISHOT_IMAGEUPLOAD__APIKEY` | chave do serviço de imagem |
| `AISHOT_HOTKEY` | ex.: `PrintScreen`, `Ctrl+Alt+S` |

## Build / Run

```sh
dotnet build src/AiShot/AiShot.csproj -c Release
dotnet run --project src/AiShot/AiShot.csproj
```

A IA de visão (se ativada) sempre roda primeiro, gerando o contexto que a IA
principal usa para responder à sua pergunta.
