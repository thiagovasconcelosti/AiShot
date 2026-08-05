# AiShot

🇺🇸 **English** · 🇧🇷 [Português](README.pt-BR.md) · 🇪🇸 [Español](README.es.md)

AI-focused clone of Lightshot/prntscr for Windows. Capture a screen region, annotate it (arrows, shapes, colors), copy / save / upload / share, and **ask an AI about the screenshot**.

📖 **Documentation:** https://thiagovasconcelosti.github.io/AiShot/ (Usage guide + Technical docs, in EN/PT/ES)

- **.NET 10 / C# / WinForms** — Windows only.
- Lives in the **system tray**. Global hotkey (default **PrintScreen**).
- In-place editing overlay (shadcn-dark aesthetic), multi-monitor.

## Features

- Region capture over the whole virtual desktop (Lightshot-style dimmed overlay).
- Editor: arrow, rectangle, ellipse, line, pen, text, color & thickness, undo (Ctrl+Z).
- **Copy** (clipboard), **Save** (PNG/JPG), **Open in Paint**, **Upload** & **Share** (freeimage.host / imgbb).
- **Ask AI** about the screenshot — continuous chat, provider `anthropic` or `openai`, automatic **fallback** and optional **vision AI**. When vision is on, it describes the image *before* the main AI answers.

## Browser Extension

Prefer capturing straight from the browser? **[AiShot for Chrome](https://github.com/thiagovasconcelosti/AIShot-Chrome-Extension)** brings the same capture-annotate-ask-AI workflow into a Manifest V3 extension — region, visible-area and full-page (scroll-stitched) capture, keyboard shortcuts, and the same visual language as the desktop app. Early stage: capture and export work today, annotation drawing and the AI chat are landing next — check the [roadmap](https://github.com/thiagovasconcelosti/AIShot-Chrome-Extension/blob/master/ROADMAP.md).

## Install

**Chocolatey** (once approved):
```sh
choco install aishot
```

**Installer / portable:** download from the [latest release](https://github.com/thiagovasconcelosti/AiShot/releases/latest):
- `AiShot-Setup-x.y.z.exe` — per-user installer (no admin), shortcuts + optional run-at-startup.
- `AiShot.exe` — single portable executable (no .NET install required).

## Configuration

API keys are **encrypted with DPAPI** and stored in `%APPDATA%\AiShot\appsettings.json` — never in plain text. Configure via the **Settings** window in the tray, or environment variables `AISHOT_*` (they take precedence). Works with OpenAI-compatible providers (OpenAI, DeepSeek, OpenRouter) and Anthropic.

See the [Usage guide](https://thiagovasconcelosti.github.io/AiShot/#/usage) and [Technical docs](https://thiagovasconcelosti.github.io/AiShot/#/technical).

## Build

```sh
dotnet build src/AiShot/AiShot.csproj -c Release
```

Self-contained single file (runs without .NET installed):
```sh
dotnet publish src/AiShot/AiShot.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none
```

## License

[MIT](LICENSE) © 2026 Thiago Vasconcelos

Third-party components bundled with the application — Phosphor Icons, the
WebView2 SDK, the .NET runtime and the packages in the settings UI — are listed
with their copyright notices in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
