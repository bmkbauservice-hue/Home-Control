import { send } from './http'

// Aufrufe an die Report-API.
// Die Typen entsprechen DailyReportResponse im Backend.

export type DailyReport = {
  // Ausgewählter UTC-Tag, z. B. "2026-10-26"
  date: string
  // Aktueller Gerätebestand – nicht der Bestand am ausgewählten Tag
  currentDeviceCount: number
  eventCount: number
  actions: { action: string; count: number }[]
  topDevices: { deviceId: string; deviceName: string; eventCount: number }[]
}

export async function getDailyReport(date: string): Promise<DailyReport> {
  const response = await send(`/api/reports/daily?date=${encodeURIComponent(date)}`)
  return (await response.json()) as DailyReport
}

// Heutiges Datum in UTC als "JJJJ-MM-TT". toISOString() rechnet immer in UTC.
export function getTodayUtc(): string {
  return new Date().toISOString().slice(0, 10)
}

// "2026-10-26" -> "26.10.2026", ohne Umweg über Date (sonst könnte die Zeitzone den Tag verschieben).
export function formatIsoDate(date: string): string {
  const [year, month, day] = date.split('-')
  return `${day}.${month}.${year}`
}
