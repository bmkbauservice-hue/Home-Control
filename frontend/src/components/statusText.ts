import type { BlinkStatusInfo, LedState, SystemStatus } from '../api/status'

// Texte für Status-Leiste und Begrüßung. Eigene Datei, weil Komponenten-Dateien nur Komponenten
// exportieren sollen (Lint-Regel react/only-export-components, wichtig für das schnelle Neuladen im Development).

export type Overall = { state: LedState; text: string }

// Ein Satz für den Gesamtzustand. Das Wichtigste zuerst: Ohne Server ist alles andere egal.
export function overallOf(
  status: SystemStatus | null,
  blink: BlinkStatusInfo | null,
  serverUnreachable: boolean,
  statusError: string | null,
): Overall {
  if (serverUnreachable) {
    return { state: 'offline', text: 'Server nicht erreichbar' }
  }
  if (statusError !== null) {
    return { state: 'warning', text: 'Status nicht abrufbar' }
  }
  if (status === null) {
    return { state: 'unknown', text: 'Status wird geprüft …' }
  }
  if (status.tuya === 'offline') {
    return { state: 'warning', text: 'Tuya-Cloud nicht erreichbar' }
  }
  if (status.devicesOffline > 0) {
    const text = status.devicesOffline === 1 ? '1 Gerät ist offline' : `${status.devicesOffline} Geräte sind offline`
    return { state: 'warning', text }
  }
  if (blink?.state === 'loginRequired') {
    return { state: 'warning', text: 'Blink braucht eine neue Anmeldung' }
  }
  return { state: 'online', text: 'Alles in Ordnung' }
}

// Welche Geräte offline sind, als halber Satz: "Bar Licht meldet sich nicht" oder
// "Bar Licht und Küche Licht melden sich nicht". Mehr als zwei Namen werden zusammengefasst.
export function offlineDetail(status: SystemStatus | null): string | null {
  const names = status?.devices.filter((d) => d.state === 'offline').map((d) => d.name) ?? []
  if (names.length === 0) return null
  if (names.length === 1) return `${names[0]} meldet sich nicht`
  if (names.length === 2) return `${names[0]} und ${names[1]} melden sich nicht`
  return `${names[0]}, ${names[1]} und ${names.length - 2} weitere melden sich nicht`
}

// Zweite Zeile der Begrüßung auf der Übersicht, passend zum Gesamtzustand.
export function heroLine(status: SystemStatus | null, overallState: LedState): string {
  const offline = status?.devicesOffline ?? 0
  if (offline === 1) return 'Ein Gerät braucht deinen Blick.'
  if (offline > 1) return `${offline} Geräte brauchen deinen Blick.`
  if (overallState === 'online') return 'Alles ruhig zu Hause.'
  return 'Hier ist dein Zuhause auf einen Blick.'
}
