import type { LedState } from '../api/status'

// Text für Screenreader und Tooltip, falls kein eigener angegeben ist.
const defaultTitles: Record<LedState, string> = {
  online: 'online',
  offline: 'offline',
  unknown: 'Status unbekannt',
  warning: 'Achtung',
}

type StatusLedProps = {
  state: LedState
  // Sichtbarer Text neben der LED. Ohne Text erscheint nur der Punkt.
  label?: string
  // Tooltip und Text für Screenreader (Standard: online/offline/...)
  title?: string
  // Größere LED, z. B. für die Gesamtanzeige oben.
  large?: boolean
}

// Eine kleine "Leuchtdiode": grün = online, rot = offline, grau = unbekannt, gelb = Achtung.
// Die Farbe allein reicht nicht (Farbsehschwäche), deshalb gibt es immer auch Text: sichtbar als
// label oder unsichtbar für Screenreader.
export function StatusLed({ state, label, title, large = false }: StatusLedProps) {
  const text = title ?? defaultTitles[state]
  const className = `led led-${state}${large ? ' led-large' : ''}`

  if (label === undefined) {
    return <span className={className} role="img" aria-label={text} title={text} />
  }

  return (
    <span className="led-label" title={text}>
      <span className={className} aria-hidden="true" />
      {label}
    </span>
  )
}
