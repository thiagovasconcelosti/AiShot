# Guía de uso

## Instalación

### Chocolatey (recomendado, tras la aprobación)
```sh
choco install aishot
```

### Instalador
Descarga `AiShot-Setup-x.y.z.exe` desde el [último release](https://github.com/thiagovasconcelosti/AiShot/releases/latest) y ejecútalo. Instala por usuario (sin admin), crea accesos directos en el Menú Inicio / escritorio y puede activar **iniciar con Windows**.

### Portable
Descarga `AiShot.exe` y ejecútalo directamente — un solo archivo, sin necesidad de instalar .NET.

## Primera ejecución

AiShot arranca en la **bandeja del sistema** (icono **A** azul). No tiene ventana principal.

- **Hacer una captura:** pulsa el atajo (**PrintScreen** por defecto) o haz doble clic en el icono de la bandeja.
- **Clic derecho en el icono** abre el menú: *Capturar*, *Configuración*, *Iniciar con Windows*, *Salir*.

## Configurar la IA

Abre **Configuración** desde el menú de la bandeja:

1. **Atajo** — haz clic en el campo y pulsa la combinación deseada (p. ej. `Ctrl+Alt+S`). *Limpiar* la borra.
2. **IA principal** — proveedor (`anthropic` u `openai`), clave de API, modelo y base URL opcional.
   También funcionan endpoints compatibles con OpenAI: usa el proveedor `openai` y la base URL, p. ej. DeepSeek (`https://api.deepseek.com`) u OpenRouter (`https://openrouter.ai/api`).
3. **IA de fallback** — se usa automáticamente si la principal falla.
4. **IA de visión (opcional)** — un modelo con visión que describe la imagen **antes** de que responda la IA principal. Necesaria cuando el modelo principal no lee imágenes (p. ej. modelos de texto de DeepSeek). Opción gratis en OpenRouter: `google/gemma-4-26b-a4b-it:free`.
5. **Subida de imágenes** — `freeimage` (freeimage.host) o `imgbb`, con clave opcional.

Las claves se guardan **cifradas** (DPAPI de Windows) en `%APPDATA%\AiShot`.

## Capturar y anotar

1. Dispara una captura — la pantalla se oscurece y arrastras para seleccionar una región.
2. La selección permanece abierta con el overlay de edición:
   - **Barra lateral (dibujo):** lápiz, flecha, línea, rectángulo, elipse, texto, color, deshacer.
   - **Barra inferior (acciones):** Copiar, Guardar, Abrir en Paint, Subir, Compartir, Preguntar a la IA, Cerrar.
3. **Mover / redimensionar** la selección con las 8 manijas blancas (o arrastrar dentro de ella cuando ninguna herramienta está activa).
4. **Deshacer** la última anotación con `Ctrl+Z`. **Esc** cancela/cierra.

## Acciones

- **Copiar** — pone la imagen final (con anotaciones) en el portapapeles.
- **Guardar** — guarda como PNG o JPG.
- **Abrir en Paint** — envía la imagen a mspaint.
- **Subir** — sube al servicio de imágenes configurado y copia la URL.
- **Compartir** — sube y abre la URL en el navegador.

> ⚠️ Subir/Compartir envían la imagen a un host **público**. Se pide confirmación la primera vez.

## Preguntar a la IA

Haz clic en **Preguntar a la IA** para abrir un globo de chat sobre la captura (no cierra la captura):

- Escribe una pregunta y pulsa **Enter**. Tus mensajes aparecen a la derecha, los de la IA a la izquierda.
- La conversación es **continua** — las preguntas siguientes mantienen el contexto.
- Si la **IA de visión** está activa, describe la imagen una vez y la IA principal responde usando esa descripción.
- **Esc** cierra el chat (no la captura); desplázate con la rueda del ratón.

## Iniciar con Windows

Actívalo desde el menú de la bandeja (**Iniciar con Windows**) o durante la instalación. Registra AiShot en el arranque del usuario actual — sin admin.
