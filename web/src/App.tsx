import { useEffect, useRef, useState } from "react"
import { Eye, EyeOff, X, Keyboard } from "lucide-react"
import { bridge, type Config, type Endpoint, type Vision } from "@/bridge"
import { Card } from "@/components/ui/card"
import { Label } from "@/components/ui/label"
import { Input } from "@/components/ui/input"
import { Switch } from "@/components/ui/switch"
import { Button } from "@/components/ui/button"
import { Separator } from "@/components/ui/separator"
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select"

const PROVIDERS = ["anthropic", "openai"]
const SERVICES = ["freeimage", "imgbb"]

export default function App() {
  const [cfg, setCfg] = useState<Config | null>(null)
  const [capturing, setCapturing] = useState(false)

  const stopCapture = () => { setCapturing(false); bridge.hotkeyStop() }

  useEffect(() => {
    bridge.onConfig(setCfg)
    bridge.onHotkey((combo) => {
      setCfg((c) => (c ? { ...c, hotKey: combo } : c))
      setCapturing(false)
      bridge.hotkeyStop()
      ;(document.activeElement as HTMLElement | null)?.blur()
    })
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== "Escape") return
      // Esc cancela a captura (se ativa) em vez de fechar a janela.
      setCapturing((cap) => {
        if (cap) { bridge.hotkeyStop(); (document.activeElement as HTMLElement | null)?.blur(); return false }
        bridge.cancel()
        return cap
      })
    }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [])

  if (!cfg) return <div className="p-6 text-muted-foreground">Carregando…</div>

  const set = (patch: Partial<Config>) => setCfg({ ...cfg, ...patch })
  const setAi = (patch: Partial<Config["ai"]>) => set({ ai: { ...cfg.ai, ...patch } })
  const setFb = (patch: Partial<Endpoint>) => setAi({ fallback: { ...cfg.ai.fallback, ...patch } })
  const setVis = (patch: Partial<Vision>) => setAi({ vision: { ...cfg.ai.vision, ...patch } })

  return (
    <div className="flex h-full flex-col bg-background text-foreground">
      <header
        onMouseDown={(e) => { if (e.button === 0) bridge.dragStart() }}
        className="flex items-center gap-3 px-5 py-3.5 border-b select-none"
      >
        <img src="icon.png" alt="" className="h-8 w-8" draggable={false} />
        <div className="flex-1">
          <h1 className="text-[15px] font-semibold leading-tight">Configurações</h1>
          <p className="text-xs text-muted-foreground">Atalho, provedores de IA e upload</p>
        </div>
        <button
          onMouseDown={(e) => e.stopPropagation()}
          onClick={() => bridge.cancel()}
          className="grid h-8 w-8 place-items-center rounded-md border border-border text-foreground/80 hover:bg-destructive hover:text-white hover:border-destructive transition-colors"
          aria-label="Fechar"
        >
          <X size={18} />
        </button>
      </header>

      <main className="flex-1 overflow-y-auto px-5 py-4 space-y-3">
        <Section title="Atalho" subtitle="Tecla global para capturar">
          <Field label="Tecla">
            <HotkeyInput
              value={cfg.hotKey}
              onChange={(v) => set({ hotKey: v })}
              onStart={() => setCapturing(true)}
              onStop={stopCapture}
            />
          </Field>
        </Section>

        <Section title="IA principal" subtitle="Modelo usado para responder">
          <Field label="Provider">
            <ProviderSelect value={cfg.ai.provider} onChange={(v) => setAi({ provider: v })} options={PROVIDERS} />
          </Field>
          <Field label="API Key"><Password value={cfg.ai.apiKey} onChange={(v) => setAi({ apiKey: v })} /></Field>
          <Field label="Modelo"><Input value={cfg.ai.model} onChange={(e) => setAi({ model: e.target.value })} /></Field>
          <Field label="Base URL"><Input value={cfg.ai.baseUrl} placeholder="opcional" onChange={(e) => setAi({ baseUrl: e.target.value })} /></Field>
        </Section>

        <Section title="IA de fallback" subtitle="Usada se a principal falhar">
          <Field label="Provider">
            <ProviderSelect value={cfg.ai.fallback.provider} onChange={(v) => setFb({ provider: v })} options={PROVIDERS} />
          </Field>
          <Field label="API Key"><Password value={cfg.ai.fallback.apiKey} onChange={(v) => setFb({ apiKey: v })} /></Field>
          <Field label="Modelo"><Input value={cfg.ai.fallback.model} onChange={(e) => setFb({ model: e.target.value })} /></Field>
        </Section>

        <Section title="IA de visão" subtitle="Descreve a imagem antes da IA principal">
          <SwitchRow label="Ativar IA de visão" checked={cfg.ai.vision.enabled} onChange={(v) => setVis({ enabled: v })} />
          <Field label="Provider">
            <ProviderSelect value={cfg.ai.vision.provider} onChange={(v) => setVis({ provider: v })} options={PROVIDERS} />
          </Field>
          <Field label="API Key"><Password value={cfg.ai.vision.apiKey} onChange={(v) => setVis({ apiKey: v })} /></Field>
          <Field label="Modelo"><Input value={cfg.ai.vision.model} onChange={(e) => setVis({ model: e.target.value })} /></Field>
        </Section>

        <Section title="Upload de imagem" subtitle="Serviço de hospedagem do print">
          <Field label="Serviço">
            <ProviderSelect value={cfg.imageUpload.service} onChange={(v) => set({ imageUpload: { ...cfg.imageUpload, service: v } })} options={SERVICES} />
          </Field>
          <Field label="API Key"><Password value={cfg.imageUpload.apiKey} onChange={(v) => set({ imageUpload: { ...cfg.imageUpload, apiKey: v } })} placeholder="opcional" /></Field>
        </Section>

        <Section title="Comportamento">
          <SwitchRow label="Fechar ao copiar" checked={cfg.closeOnCopy} onChange={(v) => set({ closeOnCopy: v })} />
        </Section>
      </main>

      <footer className="flex items-center justify-between px-5 py-3 border-t">
        <div className="flex items-center gap-2 text-xs">
          <button onClick={() => bridge.openUrl("https://github.com/thiagovasconcelosti/AiShot")} className="text-muted-foreground underline underline-offset-2 hover:text-foreground">Repositório</button>
          <span className="text-muted-foreground">·</span>
          <button onClick={() => bridge.openUrl("https://thiagovasconcelosti.github.io/AiShot/")} className="text-muted-foreground underline underline-offset-2 hover:text-foreground">Documentação</button>
        </div>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={() => bridge.cancel()}>Cancelar</Button>
          <Button onClick={() => bridge.save(cfg)}>Salvar</Button>
        </div>
      </footer>

      {/* Overlay de captura de atalho */}
      {capturing && (
        <div
          onClick={stopCapture}
          className="fixed inset-0 z-50 flex items-center justify-center bg-background/60 backdrop-blur-md animate-in fade-in duration-150"
        >
          <div className="flex flex-col items-center gap-4 rounded-2xl border bg-card/90 px-10 py-8 shadow-2xl">
            <div className="relative grid place-items-center">
              <span className="absolute h-16 w-16 animate-ping rounded-full bg-primary/20" />
              <div className="grid h-16 w-16 place-items-center rounded-full bg-primary/15 text-primary">
                <Keyboard size={30} />
              </div>
            </div>
            <div className="text-center">
              <p className="text-base font-semibold">Pressione a combinação de teclas</p>
              <p className="mt-1 text-sm text-muted-foreground">Ex.: PrintScreen, Ctrl+Alt+S · Esc para cancelar</p>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

function Section({ title, subtitle, children }: { title: string; subtitle?: string; children: React.ReactNode }) {
  return (
    <Card className="gap-0 py-4">
      <div className="px-4">
        <h2 className="text-sm font-semibold">{title}</h2>
        {subtitle && <p className="text-xs text-muted-foreground mt-0.5">{subtitle}</p>}
      </div>
      <Separator className="my-3" />
      <div className="px-4 space-y-2.5">{children}</div>
    </Card>
  )
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="grid grid-cols-[92px_1fr] items-center gap-3">
      <Label className="text-muted-foreground font-normal">{label}</Label>
      {children}
    </div>
  )
}

function SwitchRow({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <div className="flex items-center justify-between py-1">
      <Label className="font-normal">{label}</Label>
      <Switch checked={checked} onCheckedChange={onChange} />
    </div>
  )
}

function ProviderSelect({ value, onChange, options }: { value: string; onChange: (v: string) => void; options: string[] }) {
  return (
    <Select value={value} onValueChange={onChange}>
      <SelectTrigger className="w-full"><SelectValue /></SelectTrigger>
      <SelectContent>
        {options.map((o) => <SelectItem key={o} value={o}>{o}</SelectItem>)}
      </SelectContent>
    </Select>
  )
}

function Password({ value, onChange, placeholder }: { value: string; onChange: (v: string) => void; placeholder?: string }) {
  const [show, setShow] = useState(false)
  return (
    <div className="relative">
      <Input type={show ? "text" : "password"} value={value} placeholder={placeholder} onChange={(e) => onChange(e.target.value)} className="pr-9" />
      <button type="button" onClick={() => setShow(!show)} className="absolute right-2 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground">
        {show ? <EyeOff size={16} /> : <Eye size={16} />}
      </button>
    </div>
  )
}

function HotkeyInput({ value, onChange, onStart, onStop }: {
  value: string; onChange: (v: string) => void; onStart: () => void; onStop: () => void
}) {
  const ref = useRef<HTMLInputElement>(null)
  return (
    <div className="relative">
      <Input
        ref={ref}
        readOnly
        value={value}
        placeholder="Clique e pressione a tecla…"
        onFocus={() => { onStart(); bridge.hotkeyStart() }}
        onBlur={() => onStop()}
        onKeyDown={(e) => {
          if (bridge.isHost) return
          e.preventDefault()
          if (["Control", "Shift", "Alt", "Meta"].includes(e.key)) return
          const parts: string[] = []
          if (e.ctrlKey) parts.push("Ctrl")
          if (e.altKey) parts.push("Alt")
          if (e.shiftKey) parts.push("Shift")
          parts.push(e.key.length === 1 ? e.key.toUpperCase() : e.key)
          onChange(parts.join("+"))
          onStop()
          ;(e.target as HTMLInputElement).blur()
        }}
        className="pr-9 cursor-pointer"
      />
      <button type="button" onClick={() => onChange("")} className="absolute right-2 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground">
        <X size={16} />
      </button>
    </div>
  )
}
