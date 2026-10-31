import { useStatus } from '../status/statusContext'

// Verbindungsstatus von Blink, so wie ihn das Backend meldet (GET /api/blink/status).
// Die Abfrage selbst macht der StatusProvider (nur beim Start und auf Knopfdruck).
export function BlinkStatus() {
  const { blink } = useStatus()

  switch (blink?.state) {
    case 'connected':
      return (
        <span className="status status-online">
          <span className="status-dot" aria-hidden="true" />
          Verbunden
        </span>
      )
    case 'loginRequired':
      return (
        <span className="status status-stale">
          <span className="status-dot" aria-hidden="true" />
          Neue Anmeldung nötig
        </span>
      )
    case 'notConfigured':
      return (
        <span className="status status-offline">
          <span className="status-dot" aria-hidden="true" />
          Nicht eingerichtet
        </span>
      )
    default:
      return (
        <span className="status status-offline">
          <span className="status-dot" aria-hidden="true" />
          Unbekannt
        </span>
      )
  }
}
