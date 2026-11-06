import { useCallback, useEffect, useState } from 'react'
import { getScale, selectScale, type ScaleReading } from '../api/scale'
import { getTuyaDevices, type TuyaCloudDevice } from '../api/tuya'
import { ErrorMessage, LoadingMessage } from '../components/StateMessages'
import { rawScaleValue, scaleTime, scaleValue } from '../components/scaleValues'
import { useDemoMode } from '../hooks/useDemoMode'

export function ScalePage() {
  const demo = useDemoMode()
  const [reading, setReading] = useState<ScaleReading | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [devices, setDevices] = useState<TuyaCloudDevice[] | null>(null)
  const [deviceId, setDeviceId] = useState('')
  const [weightCode, setWeightCode] = useState('')

  const reload = useCallback(async () => {
    try {
      const latest = await getScale()
      setReading(latest)
      setWeightCode(latest.binding?.weightCode ?? '')
      setError(null)
    } catch (err) {
      setError((err as Error).message)
    } finally { setLoading(false) }
  }, [])
  useEffect(() => { void reload() }, [reload])

  async function chooseDevice() {
    setBusy(true)
    try {
      setDevices(await getTuyaDevices())
      setDeviceId(reading?.binding?.tuyaId ?? '')
      setError(null)
    } catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }

  async function save(tuyaId: string | null, code: string | null) {
    setBusy(true)
    try {
      await selectScale(tuyaId, code)
      setDevices(null)
      await reload()
    } catch (err) { setError((err as Error).message) }
    finally { setBusy(false) }
  }

  const weight = reading?.fields.find(field => field.code === reading.binding?.weightCode)

  return (
    <section className="scale-page">
      <header className="page-header">
        <h1>Waage</h1>
        <p className="page-subtitle">Deine zuletzt an Tuya gemeldeten Werte. Nur für Admins sichtbar.</p>
      </header>
      {error && <ErrorMessage message={error} onRetry={() => void reload()} />}
      {loading && <LoadingMessage text="Waage wird geladen …" />}
      <section className="tile scale-card" aria-label="Letzter Gewichtsstand">
        <h2>{reading?.binding?.name ?? 'Silvergear Smart Scale'}</h2>
        {weight ? (
          <>
            <strong className="scale-weight">{scaleValue(weight)}</strong>
            <p>Gewicht zuletzt an Tuya gemeldet: {scaleTime(weight.reportedAtUtc)}</p>
          </>
        ) : <p>{reading?.binding ? 'Noch kein Gewichtsdatenpunkt ausgewählt oder gemeldet.' : 'Noch keine Waage verbunden.'}</p>}
        <p className="climate-note">{reading?.note}</p>
        {reading?.retrievedAtUtc && <p className="climate-note">Cloud-Abruf: {scaleTime(reading.retrievedAtUtc)}</p>}
        <div className="scale-actions">
          <button className="button" type="button" disabled={loading || busy} onClick={() => void reload()}>Aktualisieren</button>
          <button className="button" type="button" disabled={demo || busy} onClick={() => void chooseDevice()}>Waage auswählen</button>
          {reading?.binding && <button className="button" type="button" disabled={demo || busy}
            onClick={() => void save(null, null)}>Zuordnung entfernen</button>}
        </div>
      </section>

      {devices !== null && (
        <section className="tile scale-card" aria-label="Waage verbinden">
          <h2>Waage verbinden</h2>
          <p>Wähle ausschließlich deine Waage. Fehlt sie hier, liefert das verknüpfte Cloud-Projekt sie derzeit nicht.
            Dass Bluetooth-Werte auf dem Handy sichtbar sind, reicht für diesen Zugriff nicht aus.</p>
          <label htmlFor="scale-device">Gerät aus deinem Tuya-Konto</label>
          <select id="scale-device" className="input" value={deviceId} onChange={event => setDeviceId(event.target.value)}>
            <option value="">Bitte auswählen</option>
            {devices.map(device => <option key={device.tuyaId} value={device.tuyaId}>{device.name} · {device.productName}</option>)}
          </select>
          <div className="scale-actions">
            <button type="button" className="button" disabled={!deviceId || busy} onClick={() => void save(deviceId, null)}>Verbinden</button>
            <button type="button" className="button" onClick={() => setDevices(null)}>Schließen</button>
          </div>
        </section>
      )}

      {reading?.binding && reading.fields.length > 0 && (
        <section className="tile scale-card">
          <h2>Gemeldete Werte</h2>
          <p>Die Feldnamen, Einheiten und Skalierungen stammen von Tuya. Unbekannte Werte bleiben Rohwerte.
            Jeder Zeitstempel gehört nur zum jeweiligen Feld.</p>
          <label htmlFor="scale-weight-code">Welches Zahlenfeld enthält das Gewicht?</label>
          <select id="scale-weight-code" className="input" value={weightCode} onChange={event => setWeightCode(event.target.value)}>
            <option value="">Noch nicht zugeordnet</option>
            {reading.fields.filter(field => typeof field.rawValue === 'number').map(field => (
              <option key={field.code} value={field.code}>{field.label} ({field.code}){field.unit ? ' · ' + field.unit : ''}</option>
            ))}
          </select>
          <button type="button" className="button" disabled={busy || demo} onClick={() => void save(reading.binding!.tuyaId, weightCode || null)}>Gewichtsfeld speichern</button>
          <div className="scale-table-wrap">
            <table className="scale-table">
              <thead><tr><th>Wert</th><th>Zuletzt gemeldet</th><th>Meldezeit</th></tr></thead>
              <tbody>{reading.fields.map(field => (
                <tr key={field.code}>
                  <th scope="row">{field.label}<small>{field.code}</small></th>
                  <td>{scaleValue(field)}<small>Rohwert: {rawScaleValue(field.rawValue)}</small></td>
                  <td>{scaleTime(field.reportedAtUtc)}</td>
                </tr>
              ))}</tbody>
            </table>
          </div>
        </section>
      )}
    </section>
  )
}
