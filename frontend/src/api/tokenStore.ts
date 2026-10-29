// Speicher für das Access Token und der Refresh-Ablauf.
// Bewusst ohne Import aus http.ts, damit es keinen Import-Kreis gibt (http.ts importiert diese Datei).

// Das Access Token liegt im Local Storage, damit ein Neuladen der Seite nicht ausloggt.
// Es gilt nur 15 Minuten. Das langlebige Refresh-Token ist ein HTTP-only Cookie, das JavaScript
// nie zu sehen bekommt.
const STORAGE_KEY = 'hc_access_token'

// Local Storage kann fehlen oder Fehler werfen (z. B. im privaten Fenster). Dann klappt alles
// weiter, nur ohne Merken über das Neuladen hinweg.
function readStored(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY)
  } catch {
    return null
  }
}

let accessToken: string | null = readStored()

export function getAccessToken(): string | null {
  return accessToken
}

export function setAccessToken(token: string | null): void {
  accessToken = token
  try {
    if (token === null) {
      localStorage.removeItem(STORAGE_KEY)
    } else {
      localStorage.setItem(STORAGE_KEY, token)
    }
  } catch {
    // Siehe readStored: ohne Local Storage geht es trotzdem.
  }
}

// ---------- Sitzung beendet (Refresh abgelehnt) ----------

const sessionEndedListeners = new Set<() => void>()

// Die Oberfläche meldet sich hier an und zeigt dann wieder die Anmeldeseite.
export function onSessionEnded(listener: () => void): () => void {
  sessionEndedListeners.add(listener)
  return () => sessionEndedListeners.delete(listener)
}

export function endSession(): void {
  setAccessToken(null)
  sessionEndedListeners.forEach((listener) => listener())
}

// ---------- Refresh ----------

// ok: neues Access Token gespeichert. rejected: Server hat das Refresh-Token abgelehnt
// (abgelaufen, widerrufen, Reuse). unreachable: Server nicht erreichbar, die Sitzung bleibt.
export type RefreshResult = 'ok' | 'rejected' | 'unreachable'

let refreshInFlight: Promise<RefreshResult> | null = null

// Es läuft immer nur EIN Refresh gleichzeitig; alle anderen Aufrufer warten auf dasselbe
// Ergebnis. Grund: Das Backend tauscht das Refresh-Token bei jedem Refresh aus (Rotation). Zwei
// gleichzeitige Refreshs mit demselben Token würden die Reuse Detection auslösen und alle
// Sitzungen des Benutzers beenden. React StrictMode startet Effekte im Development doppelt,
// ohne diese Sperre wäre das genau der Fall.
export function refreshAccessToken(): Promise<RefreshResult> {
  refreshInFlight ??= doRefresh().finally(() => {
    refreshInFlight = null
  })
  return refreshInFlight
}

async function doRefresh(): Promise<RefreshResult> {
  let response: Response
  try {
    // Das Refresh-Cookie schickt der Browser automatisch mit (gleiche Herkunft, Pfad /api/auth).
    response = await fetch('/api/auth/refresh', { method: 'POST' })
  } catch {
    return 'unreachable'
  }

  if (response.status === 401) {
    setAccessToken(null)
    return 'rejected'
  }
  if (!response.ok) {
    return 'unreachable'
  }

  try {
    const body = (await response.json()) as { accessToken: string }
    setAccessToken(body.accessToken)
    return 'ok'
  } catch {
    return 'unreachable'
  }
}
