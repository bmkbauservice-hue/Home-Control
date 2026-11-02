import type { Device } from '../api/devices'

// Ab diesem Akkustand wird die Anzeige gelb: Rechtzeitig Batterie tauschen, bevor der
// Türsensor ausfällt und keine Ereignisse mehr meldet.
export const lowBatteryPercent = 20

export type Measurement = {
  text: string
  // true = Hinweis (gelb), z. B. fast leerer Akku
  warning: boolean
}

// Deutsche Zahlen: Komma statt Punkt (123,4 W). Volt ohne Nachkommastelle, das reicht zum Ablesen.
const wattFormat = new Intl.NumberFormat('de-DE', { minimumFractionDigits: 1, maximumFractionDigits: 1 })
const voltFormat = new Intl.NumberFormat('de-DE', { maximumFractionDigits: 0 })

// Macht aus den Messwerten eines Geräts kurze Texte, z. B. ["0,0 W", "227 V"] oder ["Akku 83 %"].
// Leere Liste, wenn das Gerät keine Messwerte liefert. Reine Funktion: leicht zu testen.
export function measurementsOf(device: Device): Measurement[] {
  const result: Measurement[] = []
  if (device.powerWatt !== null) {
    result.push({ text: `${wattFormat.format(device.powerWatt)} W`, warning: false })
  }
  if (device.voltageVolt !== null) {
    result.push({ text: `${voltFormat.format(device.voltageVolt)} V`, warning: false })
  }
  if (device.batteryPercent !== null) {
    result.push({
      text: `Akku ${device.batteryPercent} %`,
      warning: device.batteryPercent <= lowBatteryPercent,
    })
  }
  return result
}

// Uhrzeit der Messung, z. B. "11:42 Uhr", bei älteren Werten mit Datum ("01.11., 18:05 Uhr").
export function measuredAtText(measuredAtUtc: string | null, now: Date = new Date()): string | null {
  if (measuredAtUtc === null) return null
  const measured = new Date(measuredAtUtc)
  const time = measured.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
  const sameDay = measured.toDateString() === now.toDateString()
  if (sameDay) return `${time} Uhr`
  const date = measured.toLocaleDateString('de-DE', { day: '2-digit', month: '2-digit' })
  return `${date}, ${time} Uhr`
}
