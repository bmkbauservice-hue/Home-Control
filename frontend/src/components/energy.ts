import type { Device } from '../api/devices'
import type { OnlineState } from '../api/status'

export const maxMeasurementAgeMinutes = 30

export function energyNow(devices: Device[], stateOf: (id: string) => OnlineState, now = new Date()) {
  const oldest = now.getTime() - maxMeasurementAgeMinutes * 60_000
  const rows = devices
    .filter((device) => ['Light', 'Socket', 'Switch'].includes(device.type))
    .map((device) => {
      const state = stateOf(device.id)
      const time = Date.parse(device.powerMeasuredAtUtc ?? '')
      const fresh = time >= oldest && time <= now.getTime()
      return {
        device,
        state,
        watt: state === 'online' && fresh ? device.powerWatt : null,
        kwh: device.estimatedEnergyKwh ?? null,
      }
    })

  const withPower = rows.filter((row) => row.watt !== null)
  const withEnergy = rows.filter((row) => row.kwh !== null)

  return {
    rows,
    watt: withPower.length ? withPower.reduce((sum, row) => sum + (row.watt ?? 0), 0) : null,
    kwh: withEnergy.length ? withEnergy.reduce((sum, row) => sum + (row.kwh ?? 0), 0) : null,
    deviceCount: withPower.length,
    estimateCount: withEnergy.length,
  }
}

// Für Sensoren bleibt auch ein fehlender Akkustand sichtbar.
export function batteryDevices(devices: Device[]): Device[] {
  return devices
    .filter((device) => device.batteryPercent !== null
      || device.type === 'TemperatureSensor' || device.type === 'DoorSensor')
    .sort((left, right) => (left.batteryPercent ?? 101) - (right.batteryPercent ?? 101))
}
