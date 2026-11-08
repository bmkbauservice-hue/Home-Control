import { useEffect, useRef, useState } from 'react'
import { getShellyMeters, type ShellyMeterReading } from '../api/localDevices'
import { useAutoRefresh } from '../hooks/useAutoRefresh'
import type { Device } from '../api/devices'
import { useStatus } from '../status/statusContext'
import { energyNow, lowestBattery, maxMeasurementAgeMinutes } from './energy'

// Deutsche Zahl mit einer Nachkommastelle: 42,5 W
const wattFormat = new Intl.NumberFormat('de-DE', { minimumFractionDigits: 1, maximumFractionDigits: 1 })

const kwhFormat = new Intl.NumberFormat('de-DE', { minimumFractionDigits: 3, maximumFractionDigits: 3 })

type EnergyCardProps = {
  devices: Device[]
}

// Leistung und Verbrauchsschätzung aus dem Tuya-Abgleich.
export function EnergyCard({ devices }: EnergyCardProps) {
  const { stateOf } = useStatus()
  const energy = energyNow(devices, stateOf)

  return (
    <article className="energy-card" aria-label="Stromverbrauch">
      <div className="energy-block">
        {energy.watt === null && energy.kwh === null ? (
          <p className="energy-meta">Noch keine Strommesswerte verfügbar.</p>
        ) : (
          <div className="energy-metrics">
            <div>
              <h3 className="energy-label">Leistung jetzt</h3>
              {energy.watt === null ? <p className="energy-meta">Kein aktueller Wattwert</p> : (
                <>
                  <p className="energy-value">{wattFormat.format(energy.watt)}<span className="energy-unit"> W</span></p>
                  <p className="energy-meta">
                    {energy.deviceCount} von {energy.rows.length} Geräten · bis {maxMeasurementAgeMinutes} Minuten alt
                  </p>
                </>
              )}
            </div>
            <div>
              <h3 className="energy-label">Verbrauch seit Erfassung</h3>
              {energy.kwh === null ? <p className="energy-meta">Noch keine Schätzung</p> : (
                <>
                  <p className="energy-value">{kwhFormat.format(energy.kwh)}<span className="energy-unit"> kWh</span></p>
                  <p className="energy-meta">Geschätzt · {energy.estimateCount} von {energy.rows.length} Geräten</p>
                </>
              )}
            </div>
          </div>
        )}
        {energy.rows.length > 0 && (
          <details className="energy-details">
            <summary>Geräte und Messstatus ({energy.rows.length})</summary>
            <ul className="energy-devices">
              {energy.rows.map(({ device, state, watt, kwh }) => (
                <li key={device.id}>
                  <span><strong>{device.name}</strong><small>{device.roomName}</small></span>
                  <span>
                    {watt !== null ? `${wattFormat.format(watt)} W`
                      : state === 'offline' ? 'Offline'
                      : device.powerWatt === null ? 'Kein Wattwert'
                      : 'Kein aktueller Wattwert'}
                    {kwh !== null && <small>{kwhFormat.format(kwh)} kWh geschätzt</small>}
                  </span>
                </li>
              ))}
            </ul>
          </details>
        )}
      </div>

      <LocalMeters />
    </article>
  )
}

export function BatteryCard({ devices }: EnergyCardProps) {
  const battery = lowestBattery(devices)

  return (
    <article className="energy-card" aria-label="Batterien">
      <div className="energy-block">
        <h3 className="energy-label">Batterien</h3>
        {battery === null ? (
          <p className="energy-meta">Kein Gerät meldet einen Akkustand.</p>
        ) : (
          <>
            <p className={battery.low ? 'battery-line battery-low' : 'battery-line'}>
              <span className="battery-name">{battery.device.name}</span>
              <span className="battery-percent">{battery.percent} %</span>
            </p>
            {/* Balken als <meter>: Screenreader lesen den Wert vor, die Farbe allein trägt keine Information. */}
            <meter
              className="battery-meter"
              min={0}
              max={100}
              low={21}
              optimum={100}
              value={battery.percent}
              aria-label={`Akku ${battery.device.name}`}
            />
            <p className="energy-meta">
              {battery.low ? 'Bald Batterie tauschen · ' : 'Niedrigster Stand · '}
              {battery.deviceCount === 1 ? '1 Gerät mit Akku' : `${battery.deviceCount} Geräte mit Akku`}
            </p>
          </>
        )}
      </div>
    </article>
  )
}

const shellyStates = {
  auth: 'Geräteanmeldung erforderlich',
  configuration: 'Adresse, Kanal oder Doppelzuordnung prüfen',
  unsupported: 'Shelly-Schaltkanal nicht gefunden',
  unavailable: 'Gerät nicht erreichbar',
}

function LocalMeters() {
  const [meters, setMeters] = useState<ShellyMeterReading[]>([])
  const [failed, setFailed] = useState(false)
  const active = useRef<AbortController | null>(null)

  function reload() {
    if (active.current) return
    const request = new AbortController()
    active.current = request
    void getShellyMeters(request.signal)
      .then((result) => {
        if (request.signal.aborted) return
        setMeters(result)
        setFailed(false)
      })
      .catch(() => { if (!request.signal.aborted) setFailed(true) })
      .finally(() => { if (active.current === request) active.current = null })
  }

  useEffect(() => {
    reload()
    return () => {
      active.current?.abort()
      active.current = null
    }
  }, [])
  useAutoRefresh(reload)

  if (meters.length === 0 && !failed) return null

  return (
    <div className="energy-block">
      <h3 className="energy-label">Direkt verbundene WLAN-Zähler</h3>
      {failed ? <p className="energy-meta" role="status">WLAN-Zähler konnten nicht geladen werden.</p> : (
        <ul className="energy-devices">
          {meters.map((meter) => (
            <li key={meter.id}>
              <strong>{meter.name}</strong>
              <span>
                {meter.state !== 'online' ? shellyStates[meter.state]
                  : meter.watt === null ? 'Kein Wattwert' : `${wattFormat.format(meter.watt)} W`}
                {meter.counterKwh !== null && <small>Zählerstand {kwhFormat.format(meter.counterKwh)} kWh</small>}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
