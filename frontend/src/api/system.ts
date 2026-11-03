import { send } from './http'

// Antwort von GET /api/system/info (nur Admin, entspricht SystemInfoResponse im Backend).
export type SystemInfo = {
  version: string
  // Kurzer Git-Commit (7 Zeichen), null wenn ohne Git gebaut (z. B. Docker)
  commit: string | null
  environment: string
  demoMode: boolean
  dotNetVersion: string
  startedAtUtc: string
  // nur der Dateiname, nie der Pfad
  databaseFile: string
  databaseBytes: number | null
  tuyaDefaultSyncSeconds: number
  users: number
  rooms: number
  devices: number
  events: number
}

export async function getSystemInfo(): Promise<SystemInfo> {
  const response = await send('/api/system/info')
  return (await response.json()) as SystemInfo
}
