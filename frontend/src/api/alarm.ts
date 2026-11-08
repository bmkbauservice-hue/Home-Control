import { send } from './http'

export type AlarmEvent = {
  id: string
  startedAtUtc: string
  finishedAtUtc: string | null
  // "tuer" = echter Wechsel von Zu auf Auf, "test" = von Hand ausgelöst
  trigger: string
  outcome: 'laeuft' | 'bestaetigt' | 'nicht-bestaetigt' | 'fehler'
  message: string
  scenarioName: string
  doorName: string
  blinkSystemName: string
}

export type AlarmClip = { key: string; cameraName: string; createdAtUtc: string }

export type AlarmClips = { clips: AlarmClip[]; problem: string | null }

export async function getAlarmEvents(): Promise<AlarmEvent[]> {
  return (await (await send('/api/alarm/events')).json()) as AlarmEvent[]
}

// refresh = true weckt das Sync Module. Sonst liefert das Backend die zwischengespeicherte Liste.
export async function getAlarmClips(refresh = false): Promise<AlarmClips> {
  return (await (await send(`/api/alarm/clips${refresh ? '?refresh=true' : ''}`)).json()) as AlarmClips
}

// Lädt das Video über das angemeldete Backend. Erst beim Klick auf „Abspielen“ aufrufen.
export async function getAlarmClipVideo(key: string): Promise<Blob> {
  return (await send(`/api/alarm/clips/${encodeURIComponent(key)}/video`)).blob()
}
