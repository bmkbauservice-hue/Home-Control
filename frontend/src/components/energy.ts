import type { Device } from '../api/devices'
import type { OnlineState } from '../api/status'
import { lowBatteryPercent } from './measurements'

// Messwerte, die älter sind, zählen nicht mehr als "jetzt". Der Tuya-Hintergrund-Abgleich läuft alle
// 15 Minuten, 30 Minuten lassen also einen verpassten Durchlauf zu.
export const maxMeasurementAgeMinutes = 30

export type EnergyNow = {
  // Summe der Leistung in Watt, null = kein Gerät hat gerade einen aktuellen Messwert
  watt: number | null
  // Wie viele Geräte in die Summe eingehen
  deviceCount: number
  // Zeitpunkt des neuesten berücksichtigten Messwerts (ISO, UTC)
  newestMeasuredAtUtc: string | null
}

// "Energie jetzt": Summe der Leistung aller Geräte mit aktuellem Messwert. Nicht mitgezählt werden Geräte,
// die gerade offline sind, und alte Messwerte (sonst sähe ein Wert von gestern wie einer von jetzt aus).
// Reine Funktion: bekommt alles übergeben, ändert nichts, leicht zu erklären und zu testen.
export function energyNow(devices: Device[], stateOf: (id: string) => OnlineState, now: Date = new Date()): EnergyNow {
  const oldest = now.getTime() - maxMeasurementAgeMinutes * 60_000
  const counted = devices.filter(
    (d) =>
      d.powerWatt !== null &&
      d.measuredAtUtc !== null &&
      new Date(d.measuredAtUtc).getTime() >= oldest &&
      stateOf(d.id) !== 'offline',
  )

  if (counted.length === 0) {
    return { watt: null, deviceCount: 0, newestMeasuredAtUtc: null }
  }

  const watt = counted.reduce((sum, d) => sum + (d.powerWatt ?? 0), 0)
  const newest = counted.map((d) => d.measuredAtUtc as string).sort().at(-1) ?? null
  return { watt, deviceCount: counted.length, newestMeasuredAtUtc: newest }
}

export type LowestBattery = {
  device: Device
  percent: number
  // true ab 20 % und darunter: gelb anzeigen, Batterie bald tauschen
  low: boolean
  // Wie viele Geräte überhaupt einen Akkustand melden
  deviceCount: number
}

// "Batterien": das Gerät mit dem niedrigsten Akkustand. null = kein Gerät meldet einen Akku.
export function lowestBattery(devices: Device[]): LowestBattery | null {
  const withBattery = devices.filter((d) => d.batteryPercent !== null)
  if (withBattery.length === 0) return null

  const lowest = withBattery.reduce((min, d) => ((d.batteryPercent ?? 101) < (min.batteryPercent ?? 101) ? d : min))
  const percent = lowest.batteryPercent ?? 0
  return { device: lowest, percent, low: percent <= lowBatteryPercent, deviceCount: withBattery.length }
}
