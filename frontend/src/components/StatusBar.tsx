import type { BlinkStatusInfo, LedState, SystemStatus } from '../api/status'
import { useStatus } from '../status/statusContext'
import { RefreshIcon } from './Icons'
import { StatusLed } from './StatusLed'

// Status-Leiste oben auf der Übersicht: eine große LED für den Gesamtzustand, darunter je eine
// LED für Server, Tuya-Cloud und Blink und die Anzahl der Geräte, die online, offline oder ohne
// Status sind.
export function StatusBar() {
  const { status, blink, serverUnreachable, statusError, refreshing, refresh } = useStatus()
  const overall = overallOf(status, blink, serverUnreachable, statusError)
  // Der Server hat geantwortet, wenn es eine Antwort oder eine Fehlermeldung von ihm gibt.
  const serverAnswered = !serverUnreachable && (status !== null || statusError !== null)

  return (
    <section className={`status-bar status-bar-${overall.state}`} aria-label="Systemstatus">
      <div className="status-bar-head">
        <StatusLed state={overall.state} title={overall.text} large />
        <div className="status-bar-heading">
          <p className="status-bar-title">{overall.text}</p>
          <p className="status-bar-time">{statusError ?? checkedText(status, serverUnreachable)}</p>
        </div>
        <button
          type="button"
          className="button status-bar-refresh"
          onClick={refresh}
          disabled={refreshing}
          aria-label="Status aktualisieren"
        >
          <RefreshIcon size={20} />
          <span className="status-bar-refresh-text">Aktualisieren</span>
        </button>
      </div>

      <ul className="status-bar-services">
        <li>
          <StatusLed
            state={serverUnreachable ? 'offline' : serverAnswered ? 'online' : 'unknown'}
            label="Server"
            title={serverUnreachable ? 'Server nicht erreichbar' : 'Server erreichbar'}
          />
        </li>
        <li>
          <StatusLed {...tuyaLed(status, serverUnreachable)} />
        </li>
        <li>
          <StatusLed {...blinkLed(blink)} />
        </li>
      </ul>

      {status && (
        <ul className="status-bar-counts" aria-label="Geräte">
          <li className="status-count">
            <StatusLed state="online" />
            <strong>{status.devicesOnline}</strong> online
          </li>
          <li className="status-count">
            <StatusLed state="offline" />
            <strong>{status.devicesOffline}</strong> offline
          </li>
          <li className="status-count" title="Nicht mit Tuya verknüpft oder Tuya meldet nichts">
            <StatusLed state="unknown" />
            <strong>{status.devicesUnknown}</strong> ohne Status
          </li>
        </ul>
      )}
    </section>
  )
}

type Overall = { state: LedState; text: string }

// Ein Satz für den Gesamtzustand. Das Wichtigste zuerst: Ohne Server ist alles andere egal.
function overallOf(
  status: SystemStatus | null,
  blink: BlinkStatusInfo | null,
  serverUnreachable: boolean,
  statusError: string | null,
): Overall {
  if (serverUnreachable) {
    return { state: 'offline', text: 'Server nicht erreichbar' }
  }
  if (statusError !== null) {
    return { state: 'warning', text: 'Status nicht abrufbar' }
  }
  if (status === null) {
    return { state: 'unknown', text: 'Status wird geprüft …' }
  }
  if (status.tuya === 'offline') {
    return { state: 'warning', text: 'Tuya-Cloud nicht erreichbar' }
  }
  if (status.devicesOffline > 0) {
    const text = status.devicesOffline === 1 ? '1 Gerät ist offline' : `${status.devicesOffline} Geräte sind offline`
    return { state: 'warning', text }
  }
  if (blink?.state === 'loginRequired') {
    return { state: 'warning', text: 'Blink braucht eine neue Anmeldung' }
  }
  return { state: 'online', text: 'Alles in Ordnung' }
}

function checkedText(status: SystemStatus | null, serverUnreachable: boolean): string {
  if (status === null) {
    return serverUnreachable ? 'Noch keine Verbindung zum Server' : 'Einen Moment …'
  }
  const time = new Date(status.checkedAtUtc).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
  return serverUnreachable ? `Letzter Stand von ${time} Uhr` : `Geprüft um ${time} Uhr`
}

function tuyaLed(status: SystemStatus | null, serverUnreachable: boolean) {
  const label = 'Tuya-Cloud'
  if (serverUnreachable || status === null) {
    return { state: 'unknown' as const, label, title: 'Tuya: unbekannt' }
  }
  switch (status.tuya) {
    case 'online':
      return { state: 'online' as const, label, title: 'Tuya-Cloud erreichbar' }
    case 'offline':
      return { state: 'offline' as const, label, title: 'Tuya-Cloud nicht erreichbar' }
    default:
      return { state: 'unknown' as const, label, title: 'Tuya ist nicht eingerichtet' }
  }
}

function blinkLed(blink: BlinkStatusInfo | null) {
  const label = 'Blink'
  switch (blink?.state) {
    case 'connected':
      return { state: 'online' as const, label, title: 'Blink verbunden' }
    case 'loginRequired':
      return { state: 'warning' as const, label, title: 'Blink: neue Anmeldung nötig (tools/BlinkSpike)' }
    case 'notConfigured':
      return { state: 'unknown' as const, label, title: 'Blink ist nicht eingerichtet' }
    default:
      return { state: 'unknown' as const, label, title: 'Blink: unbekannt' }
  }
}
