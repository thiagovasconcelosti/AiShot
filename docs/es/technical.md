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
 ├─ SelectionGeometry  (geometría pura: hit-test, resize/move, clamp, rotación)
 ├─ SelectionChromeRenderer (marco, manijas de redimensionado y de rotación)
 ├─ FinalImageRenderer (imagen exportada: recorte, anotaciones y giro del print)
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
| `AISHOT_HISTORY__ENABLED` | `true`/`false` (desactivado por defecto) |
| `AISHOT_HISTORY__MAXITEMS` / `__MAXSIZEMB` | límites de retención |
| `AISHOT_HOTKEY` | p. ej. `PrintScreen`, `Ctrl+Alt+S` |
| `AISHOT_LANGUAGE` | `auto`, `pt`, `en` o `es` |

## Idioma de la interfaz

Tres capas, todas siguiendo la misma configuración:

- **WinForms** (overlay, menú de la bandeja, cuadros de mensaje) lee recursos `.resx`. `Resources/Strings.resx` es el idioma de origen (portugués); `Strings.en.resx` y `Strings.es.resx` se convierten en ensamblados satélite. Un idioma sin traducción cae en el archivo neutro.
- **Interfaz web** (Configuración) usa un diccionario en `web/src/i18n.ts`, indexado por la cultura que el lado C# ya resolvió y envía en el mensaje de configuración. La página nunca elige el idioma por su cuenta — si lo hiciera, la ventana y la bandeja podrían divergir.
- **Instalador** declara `[Languages]` para los tres, con sus propios textos en `[CustomMessages]`.

`Language` es `auto` por defecto (sigue al sistema). Cambiarlo en Configuración reconstruye el menú de la bandeja al instante, así que el nuevo idioma aparece sin reiniciar.

Los mensajes de error usan marcadores (`{0}`) en lugar de concatenar texto fijo con el detalle de la excepción — concatenar dejaría media traducción y media en portugués. Una prueba falla si alguna traducción pierde el marcador.

## Historial de capturas

Desactivado por defecto. Una captura contiene lo que había en pantalla — contraseñas a la vista, conversaciones, documentos —, así que guardarla en disco es una decisión del usuario, no un comportamiento que descubre después.

Al activarlo, las capturas se escriben en `%LOCALAPPDATA%\AiShot\history` cuando el usuario copia, guarda, sube o abre el chat. Se aplican dos límites, descartando siempre las más antiguas: cantidad de elementos y espacio total en disco. La captura más reciente siempre se conserva, aunque por sí sola supere el límite de espacio — borrarla justo después de escribirla vaciaría el historial al instante.

El menú de la bandeja lista las capturas guardadas con miniatura; al hacer clic en una se copia de vuelta al portapapeles. Ese mismo menú abre la carpeta y limpia el historial (con confirmación — es irreversible, y puede ser la única copia de una captura que el usuario no guardó).

La desinstalación pregunta por separado sobre el historial y sobre la configuración. Una desinstalación silenciosa (`/VERYSILENT`) no borra ninguno de los dos.

## Reconocimiento de texto (OCR)

El botón **Copiar texto de la imagen** extrae el texto de la captura y lo coloca
en el portapapeles. Usa `Windows.Media.Ocr`, el reconocedor propio de Windows:
**la imagen no sale de la máquina y la función funciona sin red**. En una
herramienta de captura eso importa — la captura suele ser justamente del mensaje
de error, del fragmento de código o del documento que el usuario prefiere no
enviar fuera.

Requiere un idioma de reconocimiento instalado en Windows (Configuración → Hora
e Idioma → Idioma). Sin ninguno, la acción lo informa en vez de fallar en
silencio; `TextRecognizer.Disponivel` responde eso antes de ofrecer la función.

Las líneas se unen preservando el salto, en lugar de usar `OcrResult.Text`, que
aplana todo en una sola línea — el salto es lo que mantiene legible un fragmento
de código pegado.

Por eso el proyecto apunta a `net10.0-windows10.0.19041.0`: la versión del
sistema en el target es lo que habilita las APIs WinRT. La aplicación sigue
funcionando en versiones anteriores de Windows, solo que sin esta función.

## Pipeline de IA

`AiService.AskAboutImageAsync` / `IAiChatSession`:

1. **Visión (opcional):** si está activa, un modelo de visión describe la imagen **una vez** (cacheada en la sesión).
2. **Principal:** la descripción se inyecta en el system prompt; el proveedor principal responde. Con visión activa, los bytes de la imagen no se reenvían al modelo principal.
3. **Fallback:** cualquier excepción del proveedor principal activa el fallback (si está configurado).

Los proveedores implementan `IAiProvider` (`AnthropicProvider`, `OpenAiProvider`) y son clientes REST OpenAI/Anthropic sobre un `HttpClient` compartido. Los cuerpos de error HTTP se truncan antes de mostrarse.

### Streaming

`IAiProvider.StreamAsync` devuelve la respuesta en incrementos mediante Server-Sent Events (`ServerSentEvents` lee el flujo; cada proveedor aporta el extractor de su formato). El chat dibuja el texto a medida que llega, en lugar de esperar la respuesta completa.

Si el proveedor principal falla **a mitad del flujo**, el texto parcial ya mostrado se descarta antes de que el fallback empiece de cero — el callback recibe una cadena vacía para señalarlo. Empalmar el inicio de una respuesta con el final de otra produciría un texto que ninguno de los dos modelos escribió.

## Atajo global

`GlobalHotKey` usa un **hook de teclado low-level** (`WH_KEYBOARD_LL`) en lugar de `RegisterHotKey`, porque en Windows 11 `PrintScreen` está reservado por la Herramienta de Recortes y `RegisterHotKey` falla/es robado. El hook intercepta la tecla primero y la **suprime**. Un **modo de captura** permite que la ventana de Configuración lea la combinación pulsada sin disparar una captura.

## Captura y edición

El overlay congela la pantalla en un bitmap (`_background`) y la selección es un
rectángulo alineado a los ejes **más un ángulo**, aplicado alrededor del centro.

- **Giran el marco y el print.** La manija de rotación (el círculo sobre el medio
  del borde superior) gira el marco y el print juntos, en el mismo sentido; las
  barras, la paleta, el chat y la etiqueta de dimensiones siguen alineados a la
  pantalla, anclados a la selección sin giro — girar no mueve ningún control.
- **Hit-test en cualquier ángulo.** El punto del mouse entra en el sistema sin
  giro del marco (`SelectionGeometry.ToLocal`), así que las ocho manijas de
  redimensionado usan la misma matemática de siempre y el lado opuesto se queda
  quieto en pantalla. Las anotaciones viven en las coordenadas del contenido (la
  pantalla sin giro): salen pegadas al print y giran con él.
- **Imagen exportada** (`FinalImageRenderer`). Sin giro es copia 1:1 del recorte,
  píxel a píxel. Con giro, el print gira en el mismo sentido del marco, el archivo
  pasa a tener el tamaño de la caja que envuelve el print girado — nada se corta —
  y lo que sobra en las esquinas queda transparente. La vista previa usa el mismo
  giro, así que lo que se ve es lo que sale en el archivo.
- **Snap.** Arrastre libre; `Shift` gira de 15° en 15°; cerca de 0/90/180/270 el
  ángulo se pega.

### Enderezar trazos

`StrokeRegularizer` es una función pura sobre puntos y responde a dos gestos:

- **Con Shift, mientras dibuja.** La línea y la flecha se traban en el múltiplo de
  45° más cercano — el largo es la proyección del arrastre en el eje trabado, para
  acompañar el gesto en vez de saltar. El rectángulo y la elipse se vuelven
  cuadrado (el círculo es la elipse de caja cuadrada), conservando el cuadrante
  del arrastre.
- **Con Shift, al soltar el lápiz.** El trazo a mano se vuelve línea, círculo o
  rectángulo. El **orden de las pruebas** es lo que separa las formas:
  **rectángulo primero**, por ser el más específico (95% de los puntos tocando los
  bordes y los cuatro bordes cubiertos, cosa que un círculo no cumple — los puntos
  a 45° quedan en el medio); después **círculo**, con centro y radio de un ajuste
  por mínimos cuadrados (Kåsa) — el centro de la caja falla cuando el trazo no
  cierra en el mismo punto, que es lo normal al dibujar a mano — y cobertura del
  85% del giro, que acepta abertura y superposición; por último **línea**, solo en
  trazo abierto, con todos los puntos a menos del 10% del largo de la cuerda.

El reconocimiento es conservador: ante la duda el trazo sigue a mano. Garabato,
curva en "S" y trazo corto (menos de 8 puntos o 40 px de camino) no se vuelven
forma.

El comportamiento de la rotación está fijado en `RotacaoDaSelecaoTests` y
`ImagemFinalRotacionadaTests`, y el de los trazos en `StrokeRegularizerTests`;
`RotacaoVisualDump` graba muestras para revisar a ojo.

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
docs/ (este sitio)
```
