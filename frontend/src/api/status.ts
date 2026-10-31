import { send } from './http'

// Aufrufe für die Status-LEDs (GET /api/status und GET /api/blink/status, nur lesend).

// Farbe einer LED: grün, rot, grau. 'warning' (gelb) nur für Hinweise wie "Blink-Anmeldung nötig".
export type LedState = 'online' | 'offline' | 'unknown' | 'warning'

// Zustand eines Geräts laut Tuya (entspricht OnlineStates im Backend).
export type OnlineState = 'online' | 'offline' | 'unknown'

// Ist die Tuya-Cloud erreichbar?
export type TuyaState = 'online' | 'offline' | 'notConfigured'

export type DeviceOnline = {
  id: string
  name: string
  state: OnlineState
}

// Antwort von GET /api/status (entspricht StatusResponse im Backend).
export type SystemStatus = {
  checkedAtUtc: string
  tuya: TuyaState
  devicesOnline: number
  devicesOffline: number
  devicesUnknown: number
  devices: DeviceOnline[]
}

export async function getStatus(): Promise<SystemStatus> {
  const response = await send('/api/status')
  return (await response.json()) as SystemStatus
}

// Antwort von GET /api/blink/status (entspricht BlinkStatusResponse im Backend).
export type BlinkState = 'connected' | 'loginRequired' | 'notConfigured'

export type BlinkStatusInfo = {
  state: BlinkState
  lastSuccessfulFetchUtc: string | null
  note: string
}

export async function getBlinkStatus(): Promise<BlinkStatusInfo> {
  const response = await send('/api/blink/status')
  return (await response.json()) as BlinkStatusInfo
}
