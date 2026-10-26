// Gemeinsame Hilfen für alle API-Aufrufe (Räume, Geräte, …).

// So sieht eine Fehlerantwort der API aus (ValidationProblem in .NET).
type ProblemDetails = {
  title?: string
  errors?: Record<string, string[]>
}

// Liest eine verständliche Fehlermeldung aus der Antwort.
async function getErrorMessage(response: Response): Promise<string> {
  if (response.status >= 500) {
    return 'Die API ist nicht erreichbar. Läuft das Backend?'
  }
  if (response.status === 404) {
    return 'Der Eintrag wurde nicht gefunden. Wurde er inzwischen gelöscht?'
  }

  try {
    const problem = (await response.json()) as ProblemDetails
    const firstError = problem.errors ? Object.values(problem.errors)[0]?.[0] : undefined
    return firstError ?? problem.title ?? `Fehler ${response.status}`
  } catch {
    return `Fehler ${response.status}`
  }
}

// Führt einen Aufruf aus und wirft bei Netzwerkfehlern eine lesbare Meldung.
export async function send(url: string, options?: RequestInit): Promise<Response> {
  let response: Response
  try {
    response = await fetch(url, options)
  } catch {
    throw new Error('Die API ist nicht erreichbar. Läuft das Backend?')
  }

  if (!response.ok) {
    throw new Error(await getErrorMessage(response))
  }
  return response
}
