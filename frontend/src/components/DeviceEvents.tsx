import { useEffect, useState, type FormEvent } from 'react'
import {
  createDeviceEvent,
  eventActions,
  getDeviceEvents,
  getEventActionLabel,
  type DeviceEvent,
  type DeviceEventInput,
} from '../api/deviceEvents'

type DeviceEventsProps = {
  deviceId: string
  // Das Formular für Testeinträge zeigen wir nur Admins (das Backend verlangt ebenfalls Admin).
  canAdd: boolean
}

// Verlauf eines Geräts plus kleines Formular für manuelle Testeinträge.
// Es gibt noch keine Geräteverbindung: Ein Eintrag wird nur gespeichert, nichts wird geschaltet.
export function DeviceEvents({ deviceId, canAdd }: DeviceEventsProps) {
  const [events, setEvents] = useState<DeviceEvent[]>([])
  const [input, setInput] = useState<DeviceEventInput>({ action: 'turned_on', value: '' })
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getDeviceEvents(deviceId)
      .then(setEvents)
      .catch((err: Error) => setError(err.message))
      .finally(() => setLoading(false))
  }, [deviceId])

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const created = await createDeviceEvent(deviceId, input)
      // Das neue Ereignis ist das neueste, also kommt es nach oben.
      setEvents((current) => [created, ...current])
      setInput((current) => ({ ...current, value: '' }))
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="events">
      <section className="events-history" aria-label="Verlauf">
        <h4 className="field-label">Verlauf</h4>

        {loading && <p className="tile-text">Wird geladen …</p>}

        {!loading && events.length === 0 && !error && (
          <p className="empty">
            Noch keine Ereignisse. Sobald das Gerät verbunden ist, erscheinen sie hier.
            {canAdd && ' Zum Ausprobieren kannst du einen manuellen Testeintrag erfassen.'}
          </p>
        )}

        {events.length > 0 && (
          <ol className="event-list">
            {events.map((item) => (
              <li key={item.id} className="event-item">
                <time className="event-time" dateTime={item.timestamp}>
                  {formatTimestamp(item.timestamp)}
                </time>
                <span className="event-action">{getEventActionLabel(item.action)}</span>
                {item.value && <span className="event-value">{item.value}</span>}
              </li>
            ))}
          </ol>
        )}
      </section>

      {canAdd && (
      <form className="events-form" onSubmit={handleSubmit}>
        <h4 className="events-form-title">Manueller Testeintrag</h4>
        <p className="tile-text events-hint">
          Wird nur im Verlauf gespeichert. Das Gerät wird nicht geschaltet und sein Zustand bleibt
          unverändert.
        </p>

        <div className="field">
          <label className="field-label" htmlFor={`event-action-${deviceId}`}>
            Aktion
          </label>
          <select
            id={`event-action-${deviceId}`}
            className="input"
            value={input.action}
            onChange={(e) => setInput({ ...input, action: e.target.value })}
          >
            {eventActions.map((action) => (
              <option key={action.value} value={action.value}>
                {action.label}
              </option>
            ))}
          </select>
        </div>

        <div className="field">
          <label className="field-label" htmlFor={`event-value-${deviceId}`}>
            Wert (optional)
          </label>
          <input
            id={`event-value-${deviceId}`}
            className="input"
            placeholder="z. B. 21,5 °C"
            value={input.value}
            maxLength={200}
            onChange={(e) => setInput({ ...input, value: e.target.value })}
          />
        </div>

        {error && (
          <p className="alert" role="alert">
            {error}
          </p>
        )}

        <button type="submit" className="button button-primary" disabled={saving}>
          Testeintrag speichern
        </button>
      </form>
      )}
    </div>
  )
}

// Zeigt den UTC-Zeitpunkt in der lokalen Zeit des Browsers an, z. B. "26.10.2026, 19:30:05".
function formatTimestamp(timestamp: string): string {
  return new Date(timestamp).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'medium' })
}
