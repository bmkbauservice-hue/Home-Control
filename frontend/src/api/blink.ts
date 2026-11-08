import { send } from './http'

// Aufrufe für die Blink-Kameras. Alles nur lesend, mit einer bewussten Ausnahme: requestNewThumbnail().

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

// Antwort von POST /api/blink/cameras/{id}/snapshot (entspricht BlinkSnapshotResponse im Backend).
export type BlinkSnapshot = {
  // false = Blink hat den Befehl angenommen, aber nicht rechtzeitig fertig gemeldet
  completed: boolean
  thumbnailTakenAtUtc: string | null
}

// "Neues Bild": Die Kamera nimmt ein neues Vorschaubild auf. Bewusste Ausnahme von "nur lesend":
// kostet Akku, schaltet aber nichts. Nur Admin, höchstens einmal pro Minute und Kamera (sonst 429).
export async function requestNewThumbnail(cameraId: string): Promise<BlinkSnapshot> {
  const response = await send(`/api/blink/cameras/${encodeURIComponent(cameraId)}/snapshot`, { method: 'POST' })
  return (await response.json()) as BlinkSnapshot
}

// Älter als einen Tag? Dann zeigt die Karte die Aufnahmezeit gelb, damit niemand ein altes Bild für live hält.
// Antwort der Livebild-Probe. Sie zeigt nur, ob Videodaten ankamen, und spielt nichts ab.
export type BlinkLiveProbe = { videoReceived: boolean; videoFrames: number; videoBytes: number; problem: string | null }

export async function probeLiveView(cameraId: string): Promise<BlinkLiveProbe> {
  const response = await send(`/api/blink/cameras/${encodeURIComponent(cameraId)}/live-probe`, { method: 'POST' })
  return (await response.json()) as BlinkLiveProbe
}

export function isOldThumbnail(timestamp: string, now: Date = new Date()): boolean {
  const date = new Date(timestamp)
  return !Number.isNaN(date.getTime()) && now.getTime() - date.getTime() > 24 * 60 * 60 * 1000
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
