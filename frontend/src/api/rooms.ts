import { send } from './http'

// Alle Aufrufe an die Raum-API an einer Stelle.
// Die Typen entsprechen RoomResponse und RoomRequest im Backend.

export type Room = {
  id: string
  name: string
  deviceCount: number
}

const BASE_URL = '/api/rooms'

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
