import { send } from './http'

// Aufrufe an die Tuya-API des Backends (nur lesend).
// Die Zugangsdaten kennt nur das Backend, das Frontend ruft ausschließlich /api/tuya auf.
// Die Typen entsprechen TuyaDeviceStatusResponse im Backend.

export type TuyaDeviceStatus = {
  // Zeitpunkt, zu dem das Backend die Werte bei Tuya abgerufen hat (UTC, ISO 8601)
  retrievedAtUtc: string
  // Codes und Werte unverändert aus der Tuya-Cloud, z. B. { code: 'switch_1', value: false }
  status: { code: string; value: unknown }[]
  note: string
}

// Zustand eines Schaltkanals. 'unknown', wenn Tuya keinen oder keinen gültigen Wert liefert.
export type SwitchState = 'on' | 'off' | 'unknown'

export async function getTestDeviceStatus(): Promise<TuyaDeviceStatus> {
  const response = await send('/api/tuya/test-device/status')
  return (await response.json()) as TuyaDeviceStatus
}

// Liest einen Schaltkanal aus dem Status. Nur echte Booleans zählen,
// alles andere (fehlt, null, "true", 1, …) gilt als unbekannt.
export function getSwitchState(status: TuyaDeviceStatus, code = 'switch_1'): SwitchState {
  const value = status.status.find((item) => item.code === code)?.value
  if (value === true) return 'on'
  if (value === false) return 'off'
  return 'unknown'
}

// Zeitpunkt in deutscher Ortszeit, unabhängig von der Zeitzone des Browsers.
export function formatGermanTime(timestamp: string): string {
  const date = new Date(timestamp)
  if (Number.isNaN(date.getTime())) return 'unbekannt'
  return `${date.toLocaleString('de-DE', {
    timeZone: 'Europe/Berlin',
    dateStyle: 'medium',
    timeStyle: 'medium',
  })} Uhr`
}
