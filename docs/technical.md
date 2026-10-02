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
 ├─ SelectionGeometry  (pure geometry: hit-test, resize/move, clamp, rotation)
 ├─ SelectionChromeRenderer (frame, resize handles and rotation handle)
 ├─ FinalImageRenderer (exported image: crop, annotations and print rotation)
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
| `AISHOT_LANGUAGE` | `auto`, `pt`, `en` or `es` |

## Interface language

Three layers, all following the same setting:

- **WinForms** (overlay, tray menu, message boxes) reads `.resx` resources. `Resources/Strings.resx` is the source language (Portuguese); `Strings.en.resx` and `Strings.es.resx` become satellite assemblies. A language with no translation falls back to the neutral file.
- **Web UI** (Settings) uses a dictionary in `web/src/i18n.ts`, keyed by the culture the C# side already resolved and sends in the config message. The page never picks the language on its own — if it did, the window and the tray could disagree.
- **Installer** declares `[Languages]` for the three, with its own texts in `[CustomMessages]`.

`Language` defaults to `auto` (follow the system). Changing it in Settings rebuilds the tray menu straight away, so the new language shows without a restart.

Error messages use placeholders (`{0}`) rather than concatenating fixed text with the exception detail — concatenation would leave half the message translated and half in Portuguese. A test fails if a translation drops the placeholder.

## Capture history

Off by default. A screenshot carries whatever was on screen — passwords in plain sight, conversations, documents — so writing it to disk is a choice the user makes, not a behaviour they discover afterwards.

When enabled, captures are written to `%LOCALAPPDATA%\AiShot\history` as the user copies, saves, uploads, or opens the chat. Two limits apply, both discarding oldest-first: item count and total disk space. The most recent capture is always kept, even if it alone exceeds the size limit — deleting it right after writing would empty the history on the spot.

The tray menu lists the stored captures with thumbnails; clicking one copies it back to the clipboard. The same menu opens the folder and clears the history (with confirmation — it is irreversible, and this may be the only copy of a capture the user never saved).

Uninstalling asks separately about the history and the configuration. A silent uninstall (`/VERYSILENT`) deletes neither.

## Text recognition (OCR)

The **Copy text from image** toolbar button extracts the text in the capture and
puts it on the clipboard. It runs on `Windows.Media.Ocr`, the recognizer built
into Windows: **the image never leaves the machine and the feature works with no
network**. In a capture tool that matters — the screenshot is usually of the very
error message, code snippet or document the user would rather not send out.

Requires a recognition language installed in Windows (Settings → Time & Language
→ Language). Without one, the action reports it instead of failing silently;
`TextRecognizer.Disponivel` answers that question up front.

Lines are joined preserving the line breaks rather than using `OcrResult.Text`,
which flattens everything onto one line — the break is what keeps a pasted code
snippet readable.

This is why the project targets `net10.0-windows10.0.19041.0`: the OS version in
the target framework is what unlocks the WinRT APIs. The app still runs on older
Windows, just without this feature.

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

## Capture and editing

The overlay freezes the screen into a bitmap (`_background`) and the selection is
an axis-aligned rectangle **plus an angle**, applied around its centre.

- **The frame and the print turn together.** The rotation handle (the dot above
  the middle of the top edge) turns the frame and the print in the same direction;
  the toolbars, the palette, the chat and the dimensions label stay aligned to the
  screen, anchored to the unrotated selection — rotating moves no control.
- **Hit-testing at any angle.** The mouse point is mapped into the frame's
  unrotated system (`SelectionGeometry.ToLocal`), so the eight resize handles use
  the same maths as always and the opposite side stays put on screen. Annotations
  live in content coordinates (the screen without rotation): they stick to the
  print and rotate with it.
- **Exported image** (`FinalImageRenderer`). Without rotation it is a 1:1 copy of
  the crop, pixel by pixel. With rotation, the print turns in the same direction
  as the frame, the file grows to the box that encloses the rotated print —
  nothing is cut — and the leftover corners are transparent. The preview uses the
  same rotation, so what you see is what you save.
- **Snap.** Free drag; `Shift` rotates in 15° steps; near 0/90/180/270 the angle
  snaps.

### Straightening strokes

`StrokeRegularizer` is a pure function over points and answers two gestures:

- **Shift while drawing.** The line and the arrow lock to the nearest multiple of
  45° — the length is the drag projected onto the locked axis, so it follows the
  gesture instead of jumping. The rectangle and the ellipse become a square (a
  circle is the ellipse with a square box), keeping the drag quadrant.
- **Shift when releasing the pen.** The freehand stroke becomes a line, circle or
  rectangle. The **order of the tests** is what separates the shapes:
  **rectangle first**, as the most specific one (95% of the points touching the
  edges and all four edges covered — a circle fails it, because the 45° points sit
  in the middle); then **circle**, with centre and radius from a least-squares fit
  (Kåsa) — the box centre is off when the stroke does not close at the same point,
  which is the normal case for a hand-drawn circle — and 85% angular coverage,
  which accepts a gap or an overlap; and finally **line**, only for an open
  stroke, with every point within 10% of the chord length.

Recognition is conservative: when in doubt the stroke stays freehand. A scribble,
an "S" curve and a short stroke (under 8 points or 40 px of path) do not become
shapes.

The rotation behaviour is pinned by `RotacaoDaSelecaoTests` and
`ImagemFinalRotacionadaTests`, and the strokes by `StrokeRegularizerTests`;
`RotacaoVisualDump` writes samples for eyeballing.

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
  Capture/ (CaptureOverlay, ChatPanel, SelectionGeometry, SelectionChromeRenderer, FinalImageRenderer, ShapeRenderer, ToolbarLayout, Annotation)
  Ai/ (IAiProvider, AiService, AiProviderFactory, Providers/, ServerSentEvents, HttpUtil)
  Imaging/ (IImageUploader, FreeImageUploader, ImgbbUploader, ImageUploaderFactory)
  Config/ (AppConfig, SecretProtector)
  History/ (CaptureHistory)
  Ocr/ (TextRecognizer)
  Resources/ (Strings.resx, Idioma)
  HotKey/ (GlobalHotKey)
  Settings/ (SettingsForm)
  UI/ (Theme, Icons), Assets/ (Phosphor.ttf, app.ico)
installer/ (AiShot.iss, aishot.png)
chocolatey/ (aishot.nuspec, tools/)
docs/ (this site)
```
