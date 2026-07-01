# Usage guide

## Install

### Chocolatey (recommended, once approved)
```sh
choco install aishot
```

### Installer
Download `AiShot-Setup-x.y.z.exe` from the [latest release](https://github.com/thiagovasconcelosti/AiShot/releases/latest) and run it. It installs per-user (no admin), creates Start Menu / desktop shortcuts, and can enable **start with Windows**.

### Portable
Download `AiShot.exe` and run it directly — a single file, no .NET installation required.

## First run

AiShot starts in the **system tray** (blue **A** icon). It has no main window.

- **Take a screenshot:** press the hotkey (**PrintScreen** by default) or double-click the tray icon.
- **Right-click the tray icon** for the menu: *Capture*, *Settings*, *Start with Windows*, *Exit*.

## Configure the AI

Open **Settings** from the tray menu:

1. **Hotkey** — click the field and press the key combo you want (e.g. `Ctrl+Alt+S`). *Clear* resets it.
2. **Main AI** — provider (`anthropic` or `openai`), API key, model, and optional base URL.
   OpenAI-compatible endpoints work too: set provider `openai` and the base URL, e.g. DeepSeek (`https://api.deepseek.com`) or OpenRouter (`https://openrouter.ai/api`).
3. **Fallback AI** — used automatically if the main one fails.
4. **Vision AI (optional)** — a vision-capable model that describes the image **before** the main AI answers. Needed when your main model can't read images (e.g. DeepSeek text models). Free option on OpenRouter: `google/gemma-4-26b-a4b-it:free`.
5. **Image upload** — `freeimage` (freeimage.host) or `imgbb`, with an optional API key.

Keys are stored **encrypted** (Windows DPAPI) in `%APPDATA%\AiShot`.

## Capture & annotate

1. Trigger a capture — the screen dims and you drag to select a region.
2. The selection stays open with an editing overlay:
   - **Side toolbar (drawing):** pen, arrow, line, rectangle, ellipse, text, color, undo.
   - **Bottom bar (actions):** Copy, Save, Open in Paint, Upload, Share, Ask AI, Close.
3. **Move / resize** the selection with the 8 white handles (or drag inside it when no tool is active).
4. **Undo** the last annotation with `Ctrl+Z`. **Esc** cancels/closes.

## Actions

- **Copy** — puts the final image (with annotations) on the clipboard.
- **Save** — save as PNG or JPG.
- **Open in Paint** — sends the image to mspaint.
- **Upload** — uploads to the configured image host and copies the URL.
- **Share** — uploads and opens the URL in your browser.

> ⚠️ Upload/Share send the image to a **public** host. You are asked to confirm the first time.

## Ask the AI

Click **Ask AI** to open a chat bubble over the screenshot (it doesn't close the capture):

- Type a question and press **Enter**. Your messages appear on the right, the AI's on the left.
- The conversation is **continuous** — follow-up questions keep context.
- If **vision AI** is enabled, it describes the image once, then the main AI answers using that description.
- **Esc** closes the chat (not the capture); scroll with the mouse wheel.

## Start with Windows

Enable it from the tray menu (**Start with Windows**) or during installation. It registers AiShot under the current user's startup — no admin required.
