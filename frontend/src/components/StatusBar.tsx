import type { BlinkStatusInfo, SystemStatus } from '../api/status'
import { useStatus } from '../status/statusContext'
import { RefreshIcon } from './Icons'
import { offlineDetail, overallOf } from './statusText'
import { StatusLed } from './StatusLed'

// Status-Leiste oben auf der Übersicht, als ruhiger Satz: links eine farbige Kante und die Gesamt-LED,
// daneben was los ist ("1 Gerät ist offline") und welches Gerät es betrifft. Darunter eine Reihe kleiner
// Chips: Server, Tuya-Cloud, Blink und die Anzahl der Geräte online/offline/ohne Status. Auf dem Handy
// passt die Reihe nicht in die Breite, sie lässt sich dann waagerecht wischen (siehe .status-chips).
export function StatusBar() {
  const { status, blink, serverUnreachable, statusError, refreshing, refresh } = useStatus()
  const overall = overallOf(status, blink, serverUnreachable, statusError)
  // Der Server hat geantwortet, wenn es eine Antwort oder eine Fehlermeldung von ihm gibt.
  const serverAnswered = !serverUnreachable && (status !== null || statusError !== null)
  const detail = [offlineDetail(status), statusError ?? checkedText(status, serverUnreachable)].filter(Boolean).join(' · ')

  return (
    <section className={`status-bar status-bar-${overall.state}`} aria-label="Systemstatus">
      <div className="status-bar-head">
        <StatusLed state={overall.state} title={overall.text} large />
        <div className="status-bar-heading">
          <p className="status-bar-title">{overall.text}</p>
          <p className="status-bar-time">{detail}</p>
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

      {/* tabIndex: Auf dem Handy lässt sich die Reihe waagerecht scrollen, mit Tastatur nur, wenn sie fokussierbar ist. */}
      <ul className="status-chips" aria-label="Dienste und Geräte" tabIndex={0}>
        <li className="status-chip">
          <StatusLed
            state={serverUnreachable ? 'offline' : serverAnswered ? 'online' : 'unknown'}
            label="Server"
            title={serverUnreachable ? 'Server nicht erreichbar' : 'Server erreichbar'}
          />
        </li>
        <li className="status-chip">
          <StatusLed {...tuyaLed(status, serverUnreachable)} />
        </li>
        <li className="status-chip">
          <StatusLed {...blinkLed(blink)} />
        </li>
        {status && (
          <>
            <li className="status-chip">
              <StatusLed state="online" />
              <strong>{status.devicesOnline}</strong> online
            </li>
            <li className="status-chip">
              <StatusLed state="offline" />
              <strong>{status.devicesOffline}</strong> offline
            </li>
            <li className="status-chip" title="Nicht mit Tuya verknüpft oder Tuya meldet nichts">
              <StatusLed state="unknown" />
              <strong>{status.devicesUnknown}</strong> ohne Status
            </li>
          </>
        )}
      </ul>
    </section>
  )
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
      return { state: 'online' as const, label, title: `Tuya-Cloud erreichbar${tuyaAge(status.tuyaCheckedAtUtc)}` }
    case 'offline':
      return { state: 'offline' as const, label, title: 'Tuya-Cloud nicht erreichbar' }
    case 'unknown':
      return { state: 'unknown' as const, label, title: 'Tuya: noch nicht abgefragt (Hintergrund-Abgleich startet)' }
    default:
      return { state: 'unknown' as const, label, title: 'Tuya ist nicht eingerichtet' }
  }
}

// " (Stand 10:15 Uhr)": Online/Offline kommt aus dem letzten Abgleich, nicht aus diesem Moment.
function tuyaAge(checkedAtUtc: string | null): string {
  if (checkedAtUtc === null) return ''
  const time = new Date(checkedAtUtc).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
  return ` (Stand ${time} Uhr)`
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
    case 'disabled':
      return { state: 'unknown' as const, label, title: 'Blink ist in der Entwicklung aus (Blink:Enabled)' }
    default:
      return { state: 'unknown' as const, label, title: 'Blink: unbekannt' }
  }
}
