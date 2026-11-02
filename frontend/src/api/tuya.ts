import { send } from './http'

// Aufrufe an die Tuya-API des Backends (nur lesend).
// Die Zugangsdaten kennt nur das Backend, das Frontend ruft ausschließlich /api/tuya auf.

// Was beim Abgleich in unserer Datenbank passiert ist (TuyaSyncResults im Backend).
export type TuyaSyncResult = 'changed' | 'unchanged' | 'notLinked' | 'deviceNotFound' | 'unknownValue' | 'measured'

// Ein Gerät aus der Tuya-Cloud (GET /api/tuya/devices, entspricht TuyaCloudDeviceResponse).
export type TuyaCloudDevice = {
  tuyaId: string
  name: string
  productName: string
  category: string
  // So meldet es Tuya. null = Tuya sagt nichts dazu.
  online: boolean | null
  linkedDeviceId: string | null
  linkedDeviceName: string | null
}

// Alle Geräte der verknüpften Tuya-App-Konten. Nur lesend.
export async function getTuyaDevices(): Promise<TuyaCloudDevice[]> {
  const response = await send('/api/tuya/devices')
  return (await response.json()) as TuyaCloudDevice[]
}

// Ein Gerät im Ergebnis des Gesamtabgleichs (entspricht TuyaLinkedDeviceResult im Backend).
export type TuyaLinkedDeviceResult = {
  deviceId: string
  name: string
  type: string
  syncResult: TuyaSyncResult | null
  // Fehlertext, wenn Tuya für dieses eine Gerät nicht geantwortet hat
  error: string | null
}

export type TuyaLinkedDevicesSync = {
  retrievedAtUtc: string
  devices: TuyaLinkedDeviceResult[]
  note: string
}

// Liest ALLE verknüpften Geräte bei Tuya (schaltet nichts) und überträgt bekannte Zustände.
// Jedes Gerät ist ein Aufruf im Tuya-Kontingent, deshalb nur auf Knopfdruck, nie automatisch.
export async function syncLinkedDevices(): Promise<TuyaLinkedDevicesSync> {
  const response = await send('/api/tuya/sync', { method: 'POST' })
  return (await response.json()) as TuyaLinkedDevicesSync
}
