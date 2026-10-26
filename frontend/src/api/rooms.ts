// Alle Aufrufe an die Raum-API an einer Stelle.
// Die Typen entsprechen RoomResponse und RoomRequest im Backend.

export type Room = {
  id: string
  name: string
  deviceCount: number
}

const BASE_URL = '/api/rooms'

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
    return 'Der Raum wurde nicht gefunden.'
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
async function send(url: string, options?: RequestInit): Promise<Response> {
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

export async function getRooms(): Promise<Room[]> {
  const response = await send(BASE_URL)
  return (await response.json()) as Room[]
}

export async function createRoom(name: string): Promise<Room> {
  const response = await send(BASE_URL, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name }),
  })
  return (await response.json()) as Room
}

export async function updateRoom(id: string, name: string): Promise<Room> {
  const response = await send(`${BASE_URL}/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name }),
  })
  return (await response.json()) as Room
}

export async function deleteRoom(id: string): Promise<void> {
  await send(`${BASE_URL}/${id}`, { method: 'DELETE' })
}
