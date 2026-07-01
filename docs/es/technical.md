# Documentación técnica

## Stack

- **.NET 10**, **C#**, **WinForms** (`net10.0-windows`), solo Windows.
- Sin dependencias de runtime de terceros — solo la BCL (`System.Net.Http`, `System.Text.Json`, `System.Drawing`, WinForms).
- Iconos renderizados desde la fuente **Phosphor** embebida.

## Arquitectura

```
Program.Main [STAThread]
 └─ TrayAppContext (icono de bandeja, menú, atajo global, limpieza de temp)
      ├─ AppConfig (JSON + DPAPI + overrides por env)
      ├─ GlobalHotKey (hook low-level WH_KEYBOARD_LL)
      ├─ StartupManager (clave Run en HKCU)
      ├─ SettingsForm (UI de configuración)
      └─ AppHost : ICaptureServices
            ├─ AiService : IAiService  (visión → principal → fallback)
            │    ├─ AiProviderFactory → IAiProvider (Anthropic | OpenAi)
            │    └─ IAiChatSession (chat continuo)
            └─ ImageUploaderFactory → IImageUploader (FreeImage | Imgbb)

CaptureOverlay (Form) — selección + edición in situ, consume ICaptureServices
 ├─ SelectionGeometry  (geometría pura: hit-test, resize/move, clamp)
 ├─ ShapeRenderer      (dibujo de anotaciones)
 ├─ ToolbarLayout      (layout de botones, consciente del monitor)
 └─ ChatPanel          (chat de la IA: timeline, scroll, sesión)

UI/Theme, UI/Icons — helpers de dibujo (paleta shadcn-dark, fuente Phosphor)
```

Las capas de dominio (`Ai/`, `Imaging/`) no referencian WinForms — operan sobre `byte[]`/`HttpClient` detrás de interfaces, por lo que son testeables. La UI habla con ellas a través de `ICaptureServices`.

## Configuración

La config vive en `%APPDATA%\AiShot\appsettings.json`. Formato:

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

### Seguridad — almacenamiento de credenciales
Las claves de API se cifran en reposo con **DPAPI** (`ProtectedData`, `DataProtectionScope.CurrentUser`, con prefijo `enc:`). Un archivo copiado a otra máquina/usuario no se puede descifrar. Los guardados son atómicos (`.tmp` + move). Un `appsettings.json` heredado junto al ejecutable se migra a la ubicación cifrada en `%APPDATA%` en la primera carga.

### Overrides por entorno
Cada ajuste puede sobrescribirse con una variable de entorno con prefijo `AISHOT_` (máxima prioridad, nunca se escribe en disco):

| Var | Efecto |
|-----|--------|
| `AISHOT_AI__PROVIDER` | `anthropic` u `openai` |
| `AISHOT_AI__APIKEY` | clave de la IA principal |
| `AISHOT_AI__MODEL` / `AISHOT_AI__BASEURL` | modelo / base URL principal |
| `AISHOT_AI__FALLBACK__APIKEY` | clave del fallback |
| `AISHOT_AI__VISION__ENABLED` | `true`/`false` |
| `AISHOT_AI__VISION__APIKEY` / `__MODEL` | clave / modelo de visión |
| `AISHOT_IMAGEUPLOAD__SERVICE` / `__APIKEY` | host de imágenes / clave |
| `AISHOT_HOTKEY` | p. ej. `PrintScreen`, `Ctrl+Alt+S` |

## Pipeline de IA

`AiService.AskAboutImageAsync` / `IAiChatSession`:

1. **Visión (opcional):** si está activa, un modelo de visión describe la imagen **una vez** (cacheada en la sesión).
2. **Principal:** la descripción se inyecta en el system prompt; el proveedor principal responde. Con visión activa, los bytes de la imagen no se reenvían al modelo principal.
3. **Fallback:** cualquier excepción del proveedor principal activa el fallback (si está configurado).

Los proveedores implementan `IAiProvider` (`AnthropicProvider`, `OpenAiProvider`) y son clientes REST OpenAI/Anthropic sobre un `HttpClient` compartido. Los cuerpos de error HTTP se truncan antes de mostrarse.

## Atajo global

`GlobalHotKey` usa un **hook de teclado low-level** (`WH_KEYBOARD_LL`) en lugar de `RegisterHotKey`, porque en Windows 11 `PrintScreen` está reservado por la Herramienta de Recortes y `RegisterHotKey` falla/es robado. El hook intercepta la tecla primero y la **suprime**. Un **modo de captura** permite que la ventana de Configuración lea la combinación pulsada sin disparar una captura.

## Compilación y publicación

```sh
# Compilación debug
dotnet build src/AiShot/AiShot.csproj -c Debug

# Self-contained, archivo único comprimido (~49 MB, sin necesidad de .NET)
dotnet publish src/AiShot/AiShot.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none

# Framework-dependent (~0,75 MB, requiere el .NET 10 Desktop Runtime)
dotnet publish src/AiShot/AiShot.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

> Trimming/AOT **no** son compatibles con WinForms (reflexión). Los 111 MB del self-contained sin comprimir son el runtime de .NET, no la app.

## Instalador y empaquetado

- **Instalador:** script Inno Setup en `installer/AiShot.iss` (por usuario, sin admin). Compila con `ISCC.exe`.
- **Chocolatey:** paquete en `chocolatey/` — `chocolateyinstall.ps1` descarga el instalador del release y verifica su **SHA256**.

## Estructura del proyecto

```
src/AiShot/
  Program.cs, App/ (TrayAppContext, AppHost, StartupManager)
  Capture/ (CaptureOverlay, ChatPanel, SelectionGeometry, ShapeRenderer, ToolbarLayout, Annotation)
  Ai/ (IAiProvider, AiService, AiProviderFactory, Providers/, HttpUtil)
  Imaging/ (IImageUploader, FreeImageUploader, ImgbbUploader, ImageUploaderFactory)
  Config/ (AppConfig, SecretProtector)
  HotKey/ (GlobalHotKey)
  Settings/ (SettingsForm)
  UI/ (Theme, Icons), Assets/ (Phosphor.ttf, app.ico)
installer/ (AiShot.iss, aishot.png)
chocolatey/ (aishot.nuspec, tools/)
docs/ (este sitio)
```
