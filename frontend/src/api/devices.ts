import { send } from './http'

// Alle Aufrufe an die Geräte-API an einer Stelle.
// Die Typen entsprechen DeviceResponse und DeviceRequest im Backend.

export type Device = {
  id: string
  name: string
  type: string
  roomId: string
  roomName: string
  // null, solange die Geräte-Anbindung noch keine Werte liefert
  lastValue: string | null
}

// Das schickt das Frontend beim Anlegen und Bearbeiten.
export type DeviceInput = {
  name: string
  type: string
  roomId: string
}

// Gleiche Liste wie DeviceTypes im Backend, hier mit deutscher Bezeichnung.
export const deviceTypes = [
  { value: 'Light', label: 'Licht' },
  { value: 'Socket', label: 'Steckdose' },
  { value: 'TemperatureSensor', label: 'Temperatursensor' },
  { value: 'DoorSensor', label: 'Türsensor' },
  { value: 'Switch', label: 'Schalter' },
]

export function isKnownDeviceType(type: string): boolean {
  return deviceTypes.some((t) => t.value === type)
}

// Unbekannte (ältere) Typen werden unverändert angezeigt statt umbenannt.
export function getDeviceTypeLabel(type: string): string {
  return deviceTypes.find((t) => t.value === type)?.label ?? type
}

const BASE_URL = '/api/devices'

export async function getDevices(): Promise<Device[]> {
  const response = await send(BASE_URL)
  return (await response.json()) as Device[]
}

export async function createDevice(input: DeviceInput): Promise<Device> {
  const response = await send(BASE_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  return (await response.json()) as Device
}

export async function updateDevice(id: string, input: DeviceInput): Promise<Device> {
  const response = await send(`${BASE_URL}/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  return (await response.json()) as Device
}

export async function deleteDevice(id: string): Promise<void> {
  await send(`${BASE_URL}/${id}`, { method: 'DELETE' })
}
