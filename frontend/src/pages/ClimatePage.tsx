import { useCallback, useEffect, useState } from 'react'
import {
  assignBridgeSensor, getBridgeSensors, getClimate, getClimateStatus,
  type BridgeSensor, type ClimateStatus, type RoomClimate,
} from '../api/climate'
import { getRooms, type Room } from '../api/rooms'
import { useAuth } from '../auth/authContext'
import { ErrorMessage, LoadingMessage } from '../components/StateMessages'
import { useAutoRefresh } from '../hooks/useAutoRefresh'

export function climateValue(value: number | null, unit: string): string {
  return value === null ? 'Keine Messung' : value.toLocaleString('de-DE', { maximumFractionDigits: 1 }) + ' ' + unit
}

export function climateTime(value: string | null): string {
  if (!value) return 'noch keine Übertragung'
  const time = new Date(value)
  return Number.isNaN(time.getTime()) ? 'Zeitpunkt unbekannt' : time.toLocaleString('de-DE')
}

export function ClimatePage() {
  const { isAdmin } = useAuth()
  const [rooms, setRooms] = useState<Room[]>([])
  const [readings, setReadings] = useState<RoomClimate[]>([])
  const [sensors, setSensors] = useState<BridgeSensor[]>([])
  const [status, setStatus] = useState<ClimateStatus | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const reload = useCallback(async () => {
    try {
      const [allRooms, latest, detected, bridge] = await Promise.all([
        getRooms(), getClimate(), getBridgeSensors(), getClimateStatus(),
      ])
      setRooms(allRooms)
      setReadings(latest)
      setSensors(detected)
      setStatus(bridge)
      setError(null)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => { void reload() }, [reload])
  useAutoRefresh(() => void reload(), 60_000)

  async function assign(sensorId: string, roomId: string | null) {
    setBusy(sensorId)
    try {
      await assignBridgeSensor(sensorId, roomId)
      await reload()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(null)
    }
  }

  return (
    <section className="climate-page">
      <header className="page-header">
        <h1>Klima</h1>
        <p className="page-subtitle">Echte Raumtemperaturen und Luftfeuchtigkeit aus deiner Sensor-Bridge.</p>
      </header>
      {error && <ErrorMessage message={error} onRetry={() => void reload()} />}
      {loading && <LoadingMessage text="Raumklima wird geladen …" />}

      {status && (
        <section className="tile climate-info" aria-label="Status der Klima-Bridge">
          <h2>Sensorverbindung</h2>
          <p>
            {status.hasFreshReadings ? 'Neue Messwerte empfangen' :
              status.sensors > 0 ? 'Nur ältere Messwerte vorhanden' : 'Bisher keine Sensorwerte empfangen'}
            {' · '}{status.sensors} Sensoren erkannt, {status.assigned} zugeordnet
          </p>
          <p className="climate-note">Letzter Empfang: {climateTime(status.lastReceivedAtUtc)}</p>
          {!status.bridgeConfigured && (
            <p className="climate-warning">Der Sensor-Bridge-Schlüssel ist noch nicht am Backend eingerichtet.</p>
          )}
          <p className="climate-note">
            Die Google-Home-App auf dem Android-Gerät und der lokale Sensor-Gateway müssen laufen.
            Alte Werte werden ausdrücklich als veraltet gekennzeichnet.
          </p>
          <button type="button" className="button" onClick={() => void reload()}>Aktualisieren</button>
        </section>
      )}

      <section className="dashboard-section">
        <h2>Temperaturen nach Räumen</h2>
        <div className="climate-room-grid">
          {rooms.map((room) => {
            const reading = readings.find((item) => item.roomId === room.id)
            return (
              <article key={room.id} className="tile climate-room">
                <h3>{room.name}</h3>
                <strong className="climate-temperature">
                  {reading ? climateValue(reading.temperatureCelsius, '°C') : 'Keine Messung'}
                </strong>
                <p>{reading ? climateValue(reading.humidityPercent, '% Luftfeuchte') : 'Luftfeuchtigkeit unbekannt'}</p>
                {reading ? (
                  <p className={reading.stale ? 'climate-warning' : 'climate-note'}>
                    {reading.stale ? 'Älterer Wert' : 'Aktuell'} · {climateTime(reading.updatedAtUtc)}
                  </p>
                ) : <p className="climate-note">Noch kein Sensor zugeordnet oder keine Daten empfangen.</p>}
              </article>
            )
          })}
        </div>
      </section>

      <section className="tile climate-sensors" aria-label="Sensoren zuordnen">
        <h2>Google-Home-Sensoren</h2>
        {sensors.length === 0 ? (
          <p>Noch keine Sensoren empfangen. Bitte zuerst den Sensor-Gateway und die Android-App prüfen.</p>
        ) : (
          <div className="climate-sensor-list">
            {sensors.map((sensor) => (
              <div className="climate-sensor" key={sensor.sensorId}>
                <div>
                  <strong>{sensor.name}</strong>
                  <p>{climateValue(sensor.temperatureCelsius, '°C')} · {climateValue(sensor.humidityPercent, '%')}</p>
                  <small className={sensor.stale ? 'climate-warning' : 'climate-note'}>
                    {sensor.stale ? 'Veraltet' : 'Aktuell'} · {climateTime(sensor.updatedAtUtc)}
                  </small>
                </div>
                <label>
                  <span className="visually-hidden">Raum für {sensor.name}</span>
                  <select className="input" value={sensor.roomId ?? ''} disabled={!isAdmin || busy !== null}
                    onChange={(event) => void assign(sensor.sensorId, event.target.value || null)}>
                    <option value="">Nicht zugeordnet</option>
                    {rooms.map((room) => <option value={room.id} key={room.id}>{room.name}</option>)}
                  </select>
                </label>
              </div>
            ))}
          </div>
        )}
      </section>
    </section>
  )
}
