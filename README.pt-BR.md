# AiShot

🇧🇷 **Português** · 🇺🇸 [English](README.md) · 🇪🇸 [Español](README.es.md)

Clone do Lightshot/prntscr voltado para IA, para Windows. Captura uma região da tela, anota (setas, formas, cores), copia / salva / envia / compartilha, e **pergunta à IA sobre o print**.

📖 **Documentação:** https://thiagovasconcelosti.github.io/AiShot/ (Guia de uso + Documentação técnica, em EN/PT/ES)

- **.NET 10 / C# / WinForms** — apenas Windows.
- Fica na **bandeja do sistema**. Atalho global (padrão **PrintScreen**).
- Overlay de edição in-place (estética shadcn-dark), multi-monitor.

## Recursos

- Captura de região sobre toda a área virtual (overlay escurecido estilo Lightshot).
- Editor: seta, retângulo, elipse, linha, lápis, texto, cor e espessura, desfazer (Ctrl+Z).
- **Copiar** (área de transferência), **Salvar** (PNG/JPG), **Abrir no Paint**, **Upload** e **Compartilhar** (freeimage.host / imgbb).
- **Perguntar à IA** sobre o print — chat contínuo, provedor `anthropic` ou `openai`, **fallback** automático e **IA de visão** opcional. Com a visão ativa, ela descreve a imagem *antes* da IA principal responder.

## Extensão para navegador

Prefere capturar direto do navegador? A **[AiShot para Chrome](https://github.com/thiagovasconcelosti/AIShot-Chrome-Extension)** traz o mesmo fluxo de capturar-anotar-perguntar à IA numa extensão Manifest V3 — captura de região, área visível e página inteira (costurada por scroll), atalhos de teclado, e a mesma linguagem visual do app desktop. Estágio inicial: captura e exportação já funcionam, o desenho de anotações e o chat com IA estão a caminho — veja o [roadmap](https://github.com/thiagovasconcelosti/AIShot-Chrome-Extension/blob/master/ROADMAP.md).

## Instalação

**Chocolatey** (após aprovação):
```sh
choco install aishot
```

**Instalador / portátil:** baixe no [último release](https://github.com/thiagovasconcelosti/AiShot/releases/latest):
- `AiShot-Setup-x.y.z.exe` — instalador por usuário (sem admin), atalhos + iniciar com o Windows.
- `AiShot.exe` — executável único portátil (não precisa do .NET instalado).

## Configuração

As chaves de API são **cifradas com DPAPI** e guardadas em `%APPDATA%\AiShot\appsettings.json` — nunca em texto puro. Configure pela janela de **Configurações** na bandeja, ou por variáveis de ambiente `AISHOT_*` (têm precedência). Funciona com provedores compatíveis com OpenAI (OpenAI, DeepSeek, OpenRouter) e Anthropic.

Veja o [Guia de uso](https://thiagovasconcelosti.github.io/AiShot/#/pt/usage) e a [Documentação técnica](https://thiagovasconcelosti.github.io/AiShot/#/pt/technical).

## Build

```sh
dotnet build src/AiShot/AiShot.csproj -c Release
```

Arquivo único self-contained (roda sem .NET instalado):
```sh
dotnet publish src/AiShot/AiShot.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none
```

## Licença

[MIT](LICENSE) © 2026 Thiago Vasconcelos

Os componentes de terceiros distribuídos junto com o aplicativo — Phosphor
Icons, o SDK do WebView2, o runtime do .NET e os pacotes da interface de
Configurações — estão listados com seus avisos de copyright em
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
