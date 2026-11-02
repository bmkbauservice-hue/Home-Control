import type { Device } from '../api/devices'
import { useStatus } from '../status/statusContext'
import { energyNow, lowestBattery, maxMeasurementAgeMinutes } from './energy'
import { measuredAtText } from './measurements'

// Deutsche Zahl mit einer Nachkommastelle: 42,5 W
const wattFormat = new Intl.NumberFormat('de-DE', { minimumFractionDigits: 1, maximumFractionDigits: 1 })

type EnergyCardProps = {
  devices: Device[]
}

// Karte auf der Übersicht: oben "Energie jetzt" (Summe der aktuellen Leistung), darunter "Batterien"
// (niedrigster Akkustand, gelb ab 20 %). Die Werte kommen aus der Geräteliste, die der Tuya-Abgleich füllt.
export function EnergyCard({ devices }: EnergyCardProps) {
  const { stateOf } = useStatus()
  const energy = energyNow(devices, stateOf)
  const battery = lowestBattery(devices)
  const energyTime = measuredAtText(energy.newestMeasuredAtUtc)

  return (
    <article className="energy-card" aria-label="Energie und Batterien">
      <div className="energy-block">
        <h3 className="energy-label">Energie jetzt</h3>
        {energy.watt === null ? (
          <>
            <p className="energy-value energy-value-empty">–</p>
            <p className="energy-meta">Keine aktuellen Messwerte (jünger als {maxMeasurementAgeMinutes} Minuten)</p>
          </>
        ) : (
          <>
            <p className="energy-value">
              {wattFormat.format(energy.watt)}
              <span className="energy-unit"> W</span>
            </p>
            <p className="energy-meta">
              {energy.deviceCount === 1 ? 'aus 1 Gerät' : `aus ${energy.deviceCount} Geräten`}
              {energyTime && ` · Stand ${energyTime}`}
            </p>
          </>
        )}
      </div>

      <div className="energy-block energy-block-battery">
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
