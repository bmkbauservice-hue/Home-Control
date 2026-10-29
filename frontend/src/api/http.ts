// Gemeinsame Hilfen für alle API-Aufrufe (Räume, Geräte, …).
import { endSession, getAccessToken, refreshAccessToken } from './tokenStore'

// So sieht eine Fehlerantwort der API aus (ValidationProblem in .NET).
type ProblemDetails = {
  title?: string
  errors?: Record<string, string[]>
}

// Liest eine verständliche Fehlermeldung aus der Antwort.
async function getErrorMessage(response: Response): Promise<string> {
  // Manche Fehlerantworten haben keinen Inhalt, dann bleibt problem null.
  let problem: ProblemDetails | null = null
  try {
    problem = (await response.json()) as ProblemDetails
  } catch {
    problem = null
  }

  // 500 mit ProblemDetails kommt vom Backend selbst. Eine leere 500 liefert der
  // Vite-Proxy, wenn das Backend gar nicht läuft.
  if (response.status >= 500) {
    return problem?.title ?? 'Die API ist nicht erreichbar. Läuft das Backend?'
  }

  if (response.status === 403) {
    return 'Dafür fehlt dir die Berechtigung. Das darf nur ein Admin.'
  }

  if (response.status === 404) {
    return problem?.title ?? 'Der Eintrag wurde nicht gefunden. Wurde er inzwischen gelöscht?'
  }

  const firstError = problem?.errors ? Object.values(problem.errors)[0]?.[0] : undefined
  return firstError ?? problem?.title ?? `Fehler ${response.status}`
}

// Diese Aufrufe bekommen nie einen automatischen Refresh: Eine 401 heißt dort "falsche
// Zugangsdaten" bzw. "Refresh abgelehnt", nicht "Access Token abgelaufen".
const noRefreshUrls = ['/api/auth/login', '/api/auth/refresh', '/api/auth/logout']

// Ein einzelner Aufruf. Ist ein Access Token da, geht es als "Authorization: Bearer ..." mit.
async function fetchWithToken(url: string, options?: RequestInit): Promise<Response> {
  const token = getAccessToken()
  if (token === null) {
    return fetch(url, options)
  }
  const headers = new Headers(options?.headers)
  headers.set('Authorization', `Bearer ${token}`)
  return fetch(url, { ...options, headers })
}

// Führt einen Aufruf aus und wirft bei Netzwerkfehlern eine lesbare Meldung.
// Antwortet die API mit 401, weil das Access Token abgelaufen ist, holt send() einmal ein neues
// (Refresh) und wiederholt den Aufruf. Klappt der Refresh nicht, ist die Sitzung zu Ende.
export async function send(url: string, options?: RequestInit): Promise<Response> {
  let response: Response
  try {
    response = await fetchWithToken(url, options)

    if (response.status === 401 && getAccessToken() !== null && !noRefreshUrls.includes(url)) {
      const result = await refreshAccessToken()
      if (result === 'ok') {
        response = await fetchWithToken(url, options)
      } else if (result === 'rejected') {
        endSession()
      }
    }
  } catch {
    throw new Error('Die API ist nicht erreichbar. Läuft das Backend?')
  }

  if (!response.ok) {
    throw new Error(await getErrorMessage(response))
  }
  return response
}
