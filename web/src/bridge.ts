// Ponte C# (WebView2) <-> React. Sem WebView2 (npm run dev) usa mock.

export type Endpoint = { provider: string; apiKey: string; model: string; baseUrl: string }
export type Vision = Endpoint & { enabled: boolean }
export type Config = {
  appVersion?: string
  hotKey: string
  closeOnCopy: boolean
  ai: {
    provider: string
    apiKey: string
    model: string
    baseUrl: string
    fallback: Endpoint
    vision: Vision
  }
  imageUpload: { service: string; apiKey: string }
  history: History
}

/** Histórico das últimas capturas em disco. Desligado por padrão. */
export type History = { enabled: boolean; maxItems: number; maxSizeMb: number }

type Msg =
  | { type: "config"; config: Config }
  | { type: "hotkeyCaptured"; combo: string }
  | { type: "updateAvailable"; version: string; url: string }

interface WebView2 {
  postMessage: (msg: unknown) => void
  addEventListener: (t: "message", cb: (e: { data: unknown }) => void) => void
}
const wv: WebView2 | undefined = (window as unknown as { chrome?: { webview?: WebView2 } })
  .chrome?.webview

function post(msg: unknown) {
  if (wv) wv.postMessage(msg)
}

export const bridge = {
  isHost: !!wv,

  onConfig(cb: (c: Config) => void) {
    if (!wv) {
      cb(mockConfig())
      return
    }
    wv.addEventListener("message", (e) => {
      const m = e.data as Msg
      if (m && m.type === "config") cb(m.config)
    })
    post({ type: "ready" })
  },

  onHotkey(cb: (combo: string) => void) {
    if (!wv) return
    wv.addEventListener("message", (e) => {
      const m = e.data as Msg
      if (m && m.type === "hotkeyCaptured") cb(m.combo)
    })
  },

  onUpdate(cb: (version: string, url: string) => void) {
    if (!wv) return
    wv.addEventListener("message", (e) => {
      const m = e.data as Msg
      if (m && m.type === "updateAvailable") cb(m.version, m.url)
    })
  },

  startUpdate: (url: string) => post({ type: "startUpdate", url }),

  hotkeyStart: () => post({ type: "hotkeyStart" }),
  hotkeyStop: () => post({ type: "hotkeyStop" }),
  dragStart: () => post({ type: "dragStart" }),
  save: (config: Config) => post({ type: "save", config }),
  cancel: () => post({ type: "cancel" }),
  openUrl: (url: string) => post({ type: "openUrl", url }),
}

function mockConfig(): Config {
  return {
    appVersion: "0.0.0-dev",
    hotKey: "PrintScreen",
    closeOnCopy: false,
    ai: {
      provider: "openai",
      apiKey: "",
      model: "deepseek-v4-flash",
      baseUrl: "https://api.deepseek.com",
      fallback: { provider: "openai", apiKey: "", model: "google/gemini-2.5-flash", baseUrl: "https://openrouter.ai/api" },
      vision: { enabled: true, provider: "openai", apiKey: "", model: "google/gemma-4-26b-a4b-it:free", baseUrl: "https://openrouter.ai/api" },
    },
    imageUpload: { service: "freeimage", apiKey: "" },
    history: { enabled: false, maxItems: 10, maxSizeMb: 100 },
  }
}
