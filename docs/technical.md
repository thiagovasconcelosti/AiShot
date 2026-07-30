# Technical documentation

## Stack

- **.NET 10**, **C#**, **WinForms** (`net10.0-windows`), Windows only.
- No third-party runtime dependencies — only the BCL (`System.Net.Http`, `System.Text.Json`, `System.Drawing`, WinForms).
- Icons rendered from the embedded **Phosphor** font.

## Architecture

```
Program.Main [STAThread]
 └─ TrayAppContext (tray icon, menu, global hotkey, temp cleanup)
      ├─ AppConfig (JSON + DPAPI + env overrides)
      ├─ GlobalHotKey (WH_KEYBOARD_LL low-level hook)
      ├─ StartupManager (HKCU Run key)
      ├─ SettingsForm (config UI)
      └─ AppHost : ICaptureServices
            ├─ AiService : IAiService  (vision → main → fallback)
            │    ├─ AiProviderFactory → IAiProvider (Anthropic | OpenAi)
            │    └─ IAiChatSession (continuous chat)
            └─ ImageUploaderFactory → IImageUploader (FreeImage | Imgbb)

CaptureOverlay (Form) — in-place selection + editing, consumes ICaptureServices
 ├─ SelectionGeometry  (pure geometry: hit-test, resize/move, clamp)
 ├─ ShapeRenderer      (annotation drawing)
 ├─ ToolbarLayout      (toolbar button layout, monitor-aware)
 └─ ChatPanel          (AI chat: timeline, scroll, session)

UI/Theme, UI/Icons — drawing helpers (shadcn-dark palette, Phosphor font)
```

The domain layers (`Ai/`, `Imaging/`) don't reference WinForms — they operate on `byte[]`/`HttpClient` behind interfaces, so they're unit-testable. The UI talks to them through `ICaptureServices`.

## Configuration

Config lives at `%APPDATA%\AiShot\appsettings.json`. Shape:

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

### Security — credential storage
API keys are encrypted at rest with **DPAPI** (`ProtectedData`, `DataProtectionScope.CurrentUser`, prefixed `enc:`). A file copied to another machine/user can't be decrypted. Saves are atomic (`.tmp` + move). A legacy `appsettings.json` next to the executable is migrated to the encrypted `%APPDATA%` location on first load.

### Environment overrides
Every setting can be overridden by an env var prefixed `AISHOT_` (highest precedence, never written to disk):

| Var | Effect |
|-----|--------|
| `AISHOT_AI__PROVIDER` | `anthropic` or `openai` |
| `AISHOT_AI__APIKEY` | main API key |
| `AISHOT_AI__MODEL` / `AISHOT_AI__BASEURL` | main model / base URL |
| `AISHOT_AI__FALLBACK__APIKEY` | fallback key |
| `AISHOT_AI__VISION__ENABLED` | `true`/`false` |
| `AISHOT_AI__VISION__APIKEY` / `__MODEL` | vision key / model |
| `AISHOT_IMAGEUPLOAD__SERVICE` / `__APIKEY` | image host / key |
| `AISHOT_HISTORY__ENABLED` | `true`/`false` (off by default) |
| `AISHOT_HISTORY__MAXITEMS` / `__MAXSIZEMB` | retention limits |
| `AISHOT_HOTKEY` | e.g. `PrintScreen`, `Ctrl+Alt+S` |

## Capture history

Off by default. A screenshot carries whatever was on screen — passwords in plain sight, conversations, documents — so writing it to disk is a choice the user makes, not a behaviour they discover afterwards.

When enabled, captures are written to `%LOCALAPPDATA%\AiShot\history` as the user copies, saves, uploads, or opens the chat. Two limits apply, both discarding oldest-first: item count and total disk space. The most recent capture is always kept, even if it alone exceeds the size limit — deleting it right after writing would empty the history on the spot.

The tray menu lists the stored captures with thumbnails; clicking one copies it back to the clipboard. The same menu opens the folder and clears the history (with confirmation — it is irreversible, and this may be the only copy of a capture the user never saved).

Uninstalling asks separately about the history and the configuration. A silent uninstall (`/VERYSILENT`) deletes neither.

## AI pipeline

`AiService.AskAboutImageAsync` / `IAiChatSession`:

1. **Vision (optional):** if enabled, a vision model describes the image **once** (cached for the session).
2. **Main:** the description is injected into the system prompt; the main provider answers. With vision on, the image bytes are not re-sent to the main model.
3. **Fallback:** any exception from the main provider triggers the fallback provider (if configured).

Providers implement `IAiProvider` (`AnthropicProvider`, `OpenAiProvider`) and are OpenAI/Anthropic REST clients over a shared `HttpClient`. HTTP error bodies are truncated before surfacing.

### Streaming

`IAiProvider.StreamAsync` returns the answer in increments via Server-Sent Events (`ServerSentEvents` parses the stream; each provider supplies its own delta extractor). The chat renders text as it arrives instead of waiting for the full response.

If the main provider fails **mid-stream**, the partial text already displayed is discarded before the fallback starts over — the callback receives an empty string to signal it. Splicing the start of one answer onto the end of another would produce text neither model wrote.

## Global hotkey

`GlobalHotKey` uses a **low-level keyboard hook** (`WH_KEYBOARD_LL`) instead of `RegisterHotKey`, because on Windows 11 `PrintScreen` is reserved by the Snipping Tool and `RegisterHotKey` fails/gets stolen. The hook intercepts the key first and **suppresses** it. A **capture mode** lets the Settings window read a pressed combo without triggering a capture.

## Build & publish

```sh
# Debug build
dotnet build src/AiShot/AiShot.csproj -c Debug

# Self-contained, compressed single file (~49 MB, no .NET required)
dotnet publish src/AiShot/AiShot.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none

# Framework-dependent (~0.75 MB, requires .NET 10 Desktop Runtime)
dotnet publish src/AiShot/AiShot.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

> Trimming/AOT are **not** supported for WinForms (reflection). The 111 MB uncompressed self-contained size is the .NET runtime, not the app.

## Installer & packaging

- **Installer:** Inno Setup script at `installer/AiShot.iss` (per-user, no admin). Compile with `ISCC.exe`.
- **Chocolatey:** package at `chocolatey/` — `chocolateyinstall.ps1` downloads the release installer and verifies its **SHA256**.

## Project layout

```
src/AiShot/
  Program.cs, App/ (TrayAppContext, AppHost, StartupManager)
  Capture/ (CaptureOverlay, ChatPanel, SelectionGeometry, ShapeRenderer, ToolbarLayout, Annotation)
  Ai/ (IAiProvider, AiService, AiProviderFactory, Providers/, ServerSentEvents, HttpUtil)
  Imaging/ (IImageUploader, FreeImageUploader, ImgbbUploader, ImageUploaderFactory)
  Config/ (AppConfig, SecretProtector)
  History/ (CaptureHistory)
  HotKey/ (GlobalHotKey)
  Settings/ (SettingsForm)
  UI/ (Theme, Icons), Assets/ (Phosphor.ttf, app.ico)
installer/ (AiShot.iss, aishot.png)
chocolatey/ (aishot.nuspec, tools/)
docs/ (this site)
```
