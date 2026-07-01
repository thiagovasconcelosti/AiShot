# AiShot

🇪🇸 **Español** · 🇺🇸 [English](README.md) · 🇧🇷 [Português](README.pt-BR.md)

Clon de Lightshot/prntscr orientado a IA, para Windows. Captura una región de la pantalla, la anota (flechas, formas, colores), copia / guarda / sube / comparte, y **pregunta a la IA sobre la captura**.

📖 **Documentación:** https://thiagovasconcelosti.github.io/AiShot/ (Guía de uso + Documentación técnica, en EN/PT/ES)

- **.NET 10 / C# / WinForms** — solo Windows.
- Vive en la **bandeja del sistema**. Atajo global (por defecto **PrintScreen**).
- Overlay de edición in situ (estética shadcn-dark), multi-monitor.

## Características

- Captura de región sobre todo el escritorio virtual (overlay oscurecido estilo Lightshot).
- Editor: flecha, rectángulo, elipse, línea, lápiz, texto, color y grosor, deshacer (Ctrl+Z).
- **Copiar** (portapapeles), **Guardar** (PNG/JPG), **Abrir en Paint**, **Subir** y **Compartir** (freeimage.host / imgbb).
- **Preguntar a la IA** sobre la captura — chat continuo, proveedor `anthropic` u `openai`, **fallback** automático e **IA de visión** opcional. Con la visión activa, describe la imagen *antes* de que responda la IA principal.

## Instalación

**Chocolatey** (tras la aprobación):
```sh
choco install aishot
```

**Instalador / portable:** descarga desde el [último release](https://github.com/thiagovasconcelosti/AiShot/releases/latest):
- `AiShot-Setup-x.y.z.exe` — instalador por usuario (sin admin), accesos directos + iniciar con Windows.
- `AiShot.exe` — ejecutable único portable (no requiere .NET instalado).

## Configuración

Las claves de API se **cifran con DPAPI** y se guardan en `%APPDATA%\AiShot\appsettings.json` — nunca en texto plano. Configura desde la ventana de **Configuración** en la bandeja, o mediante variables de entorno `AISHOT_*` (tienen prioridad). Funciona con proveedores compatibles con OpenAI (OpenAI, DeepSeek, OpenRouter) y Anthropic.

Consulta la [Guía de uso](https://thiagovasconcelosti.github.io/AiShot/#/es/usage) y la [Documentación técnica](https://thiagovasconcelosti.github.io/AiShot/#/es/technical).

## Compilación

```sh
dotnet build src/AiShot/AiShot.csproj -c Release
```

Archivo único self-contained (funciona sin .NET instalado):
```sh
dotnet publish src/AiShot/AiShot.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -p:DebugType=none
```

## Licencia

[MIT](LICENSE) © 2026 Thiago Vasconcelos
