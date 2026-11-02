import { send } from './http'

// Aufrufe für die Blink-Kameras (nur lesend).

// Eine Kamera aus GET /api/blink/cameras (entspricht BlinkCameraResponse im Backend).
export type BlinkCamera = {
  id: string
  name: string
  // "ok" oder "low" (Blink liefert nur grob), null = unbekannt
  battery: string | null
  temperatureCelsius: number | null
  // true/false nur bei eindeutigem Status von Blink, sonst null
  online: boolean | null
  hasThumbnail: boolean
  // Aufnahmezeitpunkt des letzten Vorschaubilds, null = Blink nennt ihn nicht
  thumbnailTakenAtUtc: string | null
}

export async function getBlinkCameras(): Promise<BlinkCamera[]> {
  const response = await send('/api/blink/cameras')
  return (await response.json()) as BlinkCamera[]
}

// Das letzte Vorschaubild als Blob, plus Aufnahmezeitpunkt aus dem Header.
// Warum fetch und nicht einfach <img src="…">? Ein <img> kann keinen Authorization-Header mitschicken,
// der Endpunkt verlangt aber eine Anmeldung. send() hängt das Access Token an (und erneuert es bei Bedarf).
// Das Blink-Token und die Blink-Adresse sieht der Browser dabei nie, nur unser eigenes Backend.
export async function getBlinkThumbnail(cameraId: string): Promise<{ image: Blob; takenAtUtc: string | null }> {
  const response = await send(`/api/blink/cameras/${encodeURIComponent(cameraId)}/thumbnail`)
  return { image: await response.blob(), takenAtUtc: response.headers.get('X-Thumbnail-Taken-At') }
}

// "Akku ok" / "Akku schwach" / null (nichts anzeigen)
export function batteryText(battery: string | null): string | null {
  switch (battery) {
    case 'ok':
      return 'Akku ok'
    case 'low':
      return 'Akku schwach'
    default:
      return null
  }
}

// "02.11.2026, 15:11 Uhr" in deutscher Ortszeit, unabhängig von der Zeitzone des Browsers.
export function formatTakenAt(timestamp: string): string {
  const date = new Date(timestamp)
  if (Number.isNaN(date.getTime())) return 'unbekannt'
  return `${date.toLocaleString('de-DE', { timeZone: 'Europe/Berlin', dateStyle: 'medium', timeStyle: 'short' })} Uhr`
}
