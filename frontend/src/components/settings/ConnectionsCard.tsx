import type { BlinkState, LedState, TuyaState } from '../../api/status'
import { useStatus } from '../../status/statusContext'
import { RefreshIcon } from '../Icons'
import { StatusLed } from '../StatusLed'

const timeFormat = new Intl.DateTimeFormat('de-DE', { dateStyle: 'short', timeStyle: 'short' })

function when(timestamp: string | null): string {
  return timestamp === null ? 'noch nie' : `${timeFormat.format(new Date(timestamp))} Uhr`
}

// Was jeder Tuya-Zustand bedeutet und was zu tun ist.
const tuyaText: Record<TuyaState, { led: LedState; title: string; help: string }> = {
  online: { led: 'online', title: 'Verbunden', help: 'Der Hintergrund-Abgleich holt die Geräte regelmäßig ab.' },
  offline: {
    led: 'offline',
    title: 'Nicht erreichbar',
    help: 'Die Tuya-Cloud hat nicht geantwortet (Internet weg oder Störung bei Tuya). Der Abgleich versucht es von selbst wieder, mit wachsendem Abstand bis zu einer Stunde.',
  },
  unknown: { led: 'unknown', title: 'Wird geprüft', help: 'Der erste Abgleich nach dem Start läuft noch.' },
  notConfigured: {
    led: 'unknown',
    title: 'Nicht eingerichtet',
    help: 'Tuya-Zugangsdaten fehlen in den User Secrets (siehe docs/TUYA.md).',
  },
}

const blinkText: Record<BlinkState, { led: LedState; title: string; help: string }> = {
  connected: { led: 'online', title: 'Verbunden', help: 'Kameras und Vorschaubilder werden abgerufen.' },
  loginRequired: {
    led: 'warning',
    title: 'Neu anmelden nötig',
    help: 'Blink hat die gespeicherte Anmeldung abgelehnt, z. B. nach einer Passwortänderung bei Blink oder wenn das Token abgelaufen ist. Home Control versucht es absichtlich nicht immer wieder, sonst könnte Blink das Konto sperren.',
  },
  disabled: {
    led: 'unknown',
    title: 'In dieser Umgebung aus',
    help: 'Blink ist hier ausgeschaltet (Blink:Enabled = false, Standard beim Entwickeln). Grund: Nur EIN Prozess darf das Blink-Token erneuern.',
  },
  notConfigured: {
    led: 'unknown',
    title: 'Nicht eingerichtet',
    help: 'Noch keine Blink-Anmeldung vorhanden. Einrichten mit tools/BlinkSpike (siehe docs/BLINK.md).',
  },
}

// Verbindungen: Tuya und Blink mit Zeitpunkt der letzten erfolgreichen Abfrage und einer Erklärung.
// Liest dieselben Werte wie die Status-LEDs (StatusProvider), fragt also nicht zusätzlich bei Tuya oder Blink.
export function ConnectionsCard() {
  const { status, blink, refreshing, refresh } = useStatus()
  const tuya = status === null ? null : tuyaText[status.tuya]
  const blinkInfo = blink === null ? null : blinkText[blink.state]

  return (
    <section className="tile settings-card" aria-labelledby="settings-connections">
      <div className="settings-title-row">
        <h2 id="settings-connections" className="settings-title">
          Verbindungen
        </h2>
        <button type="button" className="button" onClick={refresh} disabled={refreshing}>
          <RefreshIcon size={18} />
          Jetzt prüfen
        </button>
      </div>

      <div className="connection">
        <StatusLed state={tuya?.led ?? 'unknown'} label={`Tuya-Cloud: ${tuya?.title ?? 'wird geladen …'}`} />
        <p className="settings-muted">Zuletzt erfolgreich: {when(status?.tuyaCheckedAtUtc ?? null)}</p>
        {tuya !== null && <p className="connection-help">{tuya.help}</p>}
      </div>

      <div className="connection">
        <StatusLed state={blinkInfo?.led ?? 'unknown'} label={`Blink: ${blinkInfo?.title ?? 'wird geladen …'}`} />
        <p className="settings-muted">Zuletzt erfolgreich: {when(blink?.lastSuccessfulFetchUtc ?? null)}</p>
        {blinkInfo !== null && <p className="connection-help">{blinkInfo.help}</p>}
        {blink?.state === 'loginRequired' && (
          <ol className="connection-steps">
            <li>
              Am PC im Projektordner ausführen: <code>dotnet run --project tools/BlinkSpike</code>
            </li>
            <li>Blink-Passwort eingeben und die PIN aus der SMS oder E-Mail.</li>
            <li>Fertig: Der Dienst übernimmt die neue Anmeldung ohne Neustart, spätestens nach etwa 2 Minuten.</li>
          </ol>
        )}
      </div>
    </section>
  )
}
