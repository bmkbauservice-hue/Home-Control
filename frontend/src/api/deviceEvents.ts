import { send } from './http'

// Aufrufe an die Ereignis-API eines Geräts.
// Die Typen entsprechen DeviceEventResponse und DeviceEventRequest im Backend.

export type DeviceEvent = {
  id: string
  // Zeitpunkt in UTC als Text, z. B. "2026-10-26T18:30:00+00:00"
  timestamp: string
  action: string
  value: string | null
}

export type DeviceEventInput = {
  action: string
  value: string
}

// Gleiche Liste wie Actions im Backend, hier mit deutscher Bezeichnung.
export const eventActions = [
  { value: 'turned_on', label: 'Eingeschaltet' },
  { value: 'turned_off', label: 'Ausgeschaltet' },
  { value: 'temperature_changed', label: 'Temperatur geändert' },
  { value: 'door_opened', label: 'Tür geöffnet' },
  { value: 'door_closed', label: 'Tür geschlossen' },
]

// Unbekannte Aktionen werden unverändert angezeigt.
export function getEventActionLabel(action: string): string {
  return eventActions.find((a) => a.value === action)?.label ?? action
}

function eventsUrl(deviceId: string): string {
  return `/api/devices/${deviceId}/events`
}

export async function getDeviceEvents(deviceId: string): Promise<DeviceEvent[]> {
  const response = await send(eventsUrl(deviceId))
  return (await response.json()) as DeviceEvent[]
}

export async function createDeviceEvent(deviceId: string, input: DeviceEventInput): Promise<DeviceEvent> {
  const response = await send(eventsUrl(deviceId), {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  return (await response.json()) as DeviceEvent
}
