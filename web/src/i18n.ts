// Traduções da tela de Configurações.
//
// O idioma vem resolvido do C# (campo resolvedLanguage), que já aplicou a
// escolha do usuário ou a cultura do sistema. A página não decide o idioma
// sozinha: se decidisse, a janela e a bandeja poderiam divergir.

export type Lang = "pt" | "en" | "es"

/** Idiomas oferecidos no seletor. "auto" segue a cultura do sistema. */
export const LANGUAGE_OPTIONS: { value: string; label: string }[] = [
  { value: "auto", label: "Automático (sistema)" },
  { value: "pt", label: "Português" },
  { value: "en", label: "English" },
  { value: "es", label: "Español" },
]

const pt = {
  headerTitle: "Configurações",
  headerSubtitle: "Atalho, provedores de IA e upload",
  close: "Fechar",

  updateAvailable: "Nova versão disponível!",
  updateHint: "Versão {0} — clique para atualizar automaticamente.",
  updateButton: "Atualizar",
  updateDownloading: "Baixando…",

  sectionShortcut: "Atalho",
  sectionShortcutSubtitle: "Tecla global para capturar",
  fieldKey: "Tecla",
  hotkeyPlaceholder: "Clique e pressione a tecla…",
  hotkeyPrompt: "Pressione a combinação de teclas",
  hotkeyHint: "Ex.: PrintScreen, Ctrl+Alt+S · Esc para cancelar",

  sectionAi: "IA principal",
  sectionAiSubtitle: "Modelo usado para responder",
  sectionFallback: "IA de fallback",
  sectionFallbackSubtitle: "Usada se a principal falhar",
  sectionVision: "IA de visão",
  sectionVisionSubtitle: "Descreve a imagem antes da IA principal",
  enableVision: "Ativar IA de visão",

  fieldProvider: "Provider",
  fieldApiKey: "API Key",
  fieldModel: "Modelo",
  fieldBaseUrl: "Base URL",
  optional: "opcional",

  sectionUpload: "Upload de imagem",
  sectionUploadSubtitle: "Serviço de hospedagem do print",
  fieldService: "Serviço",

  sectionHistory: "Histórico de capturas",
  sectionHistorySubtitle: "Guarda as últimas capturas em disco para recuperar pela bandeja",
  enableHistory: "Guardar histórico",
  fieldMaxItems: "Máx. itens",
  fieldMaxSize: "Máx. espaço",
  historyWarning:
    "Capturas podem conter dados sensíveis. Elas ficam em disco até serem substituídas pelos limites acima ou apagadas em Histórico → Limpar, no menu da bandeja.",

  sectionLanguage: "Idioma",
  sectionLanguageSubtitle: "Idioma da interface do aplicativo",
  fieldLanguage: "Idioma",
  languageRestartHint:
    "A janela de captura e o menu da bandeja passam a usar o novo idioma ao salvar.",

  sectionBehavior: "Comportamento",
  closeOnCopy: "Fechar ao copiar",
  disableScreenDimming: "Não escurecer a tela durante o print",

  repository: "Repositório",
  documentation: "Documentação",
  cancel: "Cancelar",
  save: "Salvar",
  loading: "Carregando…",
}

/** As traduções seguem a forma do português — o TypeScript cobra chave faltante. */
type Dict = typeof pt

const en: Dict = {
  headerTitle: "Settings",
  headerSubtitle: "Shortcut, AI providers and upload",
  close: "Close",

  updateAvailable: "New version available!",
  updateHint: "Version {0} — click to update automatically.",
  updateButton: "Update",
  updateDownloading: "Downloading…",

  sectionShortcut: "Shortcut",
  sectionShortcutSubtitle: "Global capture key",
  fieldKey: "Key",
  hotkeyPlaceholder: "Click and press the key…",
  hotkeyPrompt: "Press the key combination",
  hotkeyHint: "E.g.: PrintScreen, Ctrl+Alt+S · Esc to cancel",

  sectionAi: "Main AI",
  sectionAiSubtitle: "Model used to answer",
  sectionFallback: "Fallback AI",
  sectionFallbackSubtitle: "Used if the main one fails",
  sectionVision: "Vision AI",
  sectionVisionSubtitle: "Describes the image before the main AI",
  enableVision: "Enable vision AI",

  fieldProvider: "Provider",
  fieldApiKey: "API key",
  fieldModel: "Model",
  fieldBaseUrl: "Base URL",
  optional: "optional",

  sectionUpload: "Image upload",
  sectionUploadSubtitle: "Screenshot hosting service",
  fieldService: "Service",

  sectionHistory: "Capture history",
  sectionHistorySubtitle: "Keeps recent captures on disk, restorable from the tray",
  enableHistory: "Keep history",
  fieldMaxItems: "Max items",
  fieldMaxSize: "Max size",
  historyWarning:
    "Captures may contain sensitive data. They stay on disk until replaced by the limits above or deleted from History → Clear, in the tray menu.",

  sectionLanguage: "Language",
  sectionLanguageSubtitle: "Application interface language",
  fieldLanguage: "Language",
  languageRestartHint:
    "The capture window and the tray menu switch to the new language when you save.",

  sectionBehavior: "Behavior",
  closeOnCopy: "Close after copying",
  disableScreenDimming: "Do not dim the screen during capture",

  repository: "Repository",
  documentation: "Documentation",
  cancel: "Cancel",
  save: "Save",
  loading: "Loading…",
}

const es: Dict = {
  headerTitle: "Configuración",
  headerSubtitle: "Atajo, proveedores de IA y subida",
  close: "Cerrar",

  updateAvailable: "¡Nueva versión disponible!",
  updateHint: "Versión {0} — haz clic para actualizar automáticamente.",
  updateButton: "Actualizar",
  updateDownloading: "Descargando…",

  sectionShortcut: "Atajo",
  sectionShortcutSubtitle: "Tecla global para capturar",
  fieldKey: "Tecla",
  hotkeyPlaceholder: "Haz clic y pulsa la tecla…",
  hotkeyPrompt: "Pulsa la combinación de teclas",
  hotkeyHint: "Ej.: PrintScreen, Ctrl+Alt+S · Esc para cancelar",

  sectionAi: "IA principal",
  sectionAiSubtitle: "Modelo usado para responder",
  sectionFallback: "IA de reserva",
  sectionFallbackSubtitle: "Se usa si la principal falla",
  sectionVision: "IA de visión",
  sectionVisionSubtitle: "Describe la imagen antes de la IA principal",
  enableVision: "Activar IA de visión",

  fieldProvider: "Proveedor",
  fieldApiKey: "Clave de API",
  fieldModel: "Modelo",
  fieldBaseUrl: "URL base",
  optional: "opcional",

  sectionUpload: "Subida de imagen",
  sectionUploadSubtitle: "Servicio de alojamiento de la captura",
  fieldService: "Servicio",

  sectionHistory: "Historial de capturas",
  sectionHistorySubtitle: "Guarda las últimas capturas en disco, recuperables desde la bandeja",
  enableHistory: "Guardar historial",
  fieldMaxItems: "Máx. elementos",
  fieldMaxSize: "Máx. espacio",
  historyWarning:
    "Las capturas pueden contener datos sensibles. Permanecen en disco hasta que los límites de arriba las sustituyan o las borres en Historial → Limpiar, en el menú de la bandeja.",

  sectionLanguage: "Idioma",
  sectionLanguageSubtitle: "Idioma de la interfaz de la aplicación",
  fieldLanguage: "Idioma",
  languageRestartHint:
    "La ventana de captura y el menú de la bandeja pasan al nuevo idioma al guardar.",

  sectionBehavior: "Comportamiento",
  closeOnCopy: "Cerrar al copiar",
  disableScreenDimming: "No oscurecer la pantalla durante la captura",

  repository: "Repositorio",
  documentation: "Documentación",
  cancel: "Cancelar",
  save: "Guardar",
  loading: "Cargando…",
}

const DICTS: Record<Lang, Dict> = { pt, en, es }

/**
 * Dicionário do idioma informado. Idiomas sem tradução caem no português,
 * que é o idioma-fonte do projeto — o mesmo comportamento do lado C#.
 */
export function dict(lang: string | undefined): Dict {
  const curto = (lang ?? "").slice(0, 2).toLowerCase()
  return DICTS[curto as Lang] ?? pt
}

/** Substitui {0}, {1}… pelos argumentos, na ordem. */
export function format(molde: string, ...args: (string | number)[]): string {
  return molde.replace(/\{(\d+)\}/g, (inteiro, i) => {
    const v = args[Number(i)]
    return v === undefined ? inteiro : String(v)
  })
}
