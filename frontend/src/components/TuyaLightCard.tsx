import { useEffect, useState } from 'react'
import { formatGermanTime, getSwitchState, syncTestDevice, type SwitchState, type TuyaDeviceSync, type TuyaSyncResult } from '../api/tuya'
import { LampIcon } from './Icons'

const switchLabels: Record<SwitchState, string> = {
  on: 'EIN',
  off: 'AUS',
  unknown: 'Unbekannt',
}

// Kurzer Hinweis, was der letzte Abgleich in der Datenbank bewirkt hat.
const syncLabels: Record<TuyaSyncResult, string> = {
  changed: 'Neuer Zustand im Geräteverlauf gespeichert.',
  unchanged: 'Zustand unverändert, kein neues Ereignis gespeichert.',
  notLinked: 'Kein Gerät verknüpft, es wird nichts gespeichert.',
  deviceNotFound: 'Das verknüpfte Gerät wurde nicht gefunden, es wird nichts gespeichert.',
  unknownValue: 'Zustand unbekannt, es wird nichts gespeichert.',
}

// Karte für das konfigurierte Tuya-Testgerät. Zeigt nur den zuletzt von Tuya gemeldeten
// Zustand von switch_1 – kein Schalten, kein automatisches Aktualisieren.
// Jeder Abruf läuft über /sync: Ändert sich der Zustand, speichert das Backend ihn beim
// verknüpften Gerät (LastValue) und als Ereignis im Verlauf.
// Bewusst keine Online-Anzeige: Ein erfolgreicher Abruf sagt nichts darüber, ob das Gerät erreichbar ist.
export function TuyaLightCard() {
  // Letzter erfolgreicher Abruf. Bleibt bei einem Fehler erhalten und wird dann als veraltet markiert.
  const [status, setStatus] = useState<TuyaDeviceSync | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  // Wird beim Klick auf "Aktualisieren" erhöht und löst so einen neuen Abruf aus.
  const [reloadKey, setReloadKey] = useState(0)

  useEffect(() => {
    // ignore = true, wenn die Karte verschwindet, bevor die Antwort da ist.
    let ignore = false

    syncTestDevice()
      .then((loaded) => {
        if (ignore) return
        setStatus(loaded)
        setError(null)
      })
      .catch((err: Error) => {
        if (!ignore) setError(err.message)
      })
      .finally(() => {
        if (!ignore) setLoading(false)
      })

    return () => {
      ignore = true
    }
  }, [reloadKey])

  function refresh() {
    setLoading(true)
    setReloadKey((key) => key + 1)
  }

  const state: SwitchState = status ? getSwitchState(status) : 'unknown'
  // Alte Werte sind veraltet, sobald die letzte Aktualisierung fehlgeschlagen ist.
  const stale = status !== null && error !== null

  let value = switchLabels[state]
  if (!status && loading) value = '…'

  let valueClass = `light-value light-value-${state}`
  if (stale) valueClass += ' light-value-stale'

  let timeText = loading ? 'Zustand wird abgerufen …' : 'Noch kein erfolgreicher Abruf.'
  if (status) timeText = `Abgerufen: ${formatGermanTime(status.retrievedAtUtc)}`

  return (
    <article className="light-card" aria-busy={loading}>
      <div className="light-header">
        <span className="stat-icon">
          <LampIcon size={22} />
        </span>
        <span className="light-heading">
          <h3 className="light-name">Licht Wohnzimmer</h3>
          <span className="light-source">Tuya-Testgerät</span>
        </span>
        {stale && <span className="status status-stale">Veraltet</span>}
      </div>

      <div className="light-state" aria-live="polite">
        <span className="light-label">Zuletzt von Tuya gemeldeter Zustand</span>
        <span className={valueClass}>{value}</span>
        <span className="light-time">{timeText}</span>
      </div>

      {error && (
        <p className="alert light-alert" role="alert">
          {stale ? 'Aktualisierung fehlgeschlagen, angezeigt wird der alte Stand. ' : ''}
          {error}
        </p>
      )}

      <button type="button" className="button" onClick={refresh} disabled={loading}>
        {loading ? 'Wird aktualisiert …' : 'Aktualisieren'}
      </button>

      {status && !stale && <p className="light-hint">{syncLabels[status.syncResult] ?? ''}</p>}

      <p className="light-hint">Ob das Gerät gerade online ist, lässt sich daraus nicht ablesen.</p>
    </article>
  )
}
