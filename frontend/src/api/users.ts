import type { Role } from './auth'
import { send } from './http'

// Benutzer verwalten (Einstellungsseite, nur Admin) und eigenes Passwort ändern.

// Eine Zeile aus GET /api/users (entspricht UserListResponse im Backend).
export type UserListItem = {
  id: string
  email: string
  role: Role
  createdAtUtc: string
  // nach 5 falschen Passwörtern kurz gesperrt
  lockedOut: boolean
}

const JSON_HEADERS = { 'Content-Type': 'application/json' }

export async function getUsers(): Promise<UserListItem[]> {
  const response = await send('/api/users')
  return (await response.json()) as UserListItem[]
}

// Anlegen läuft über denselben Endpunkt wie das erste Konto; als Admin darf man die Rolle wählen.
export async function createUser(email: string, password: string, role: Role): Promise<void> {
  await send('/api/auth/register', { method: 'POST', headers: JSON_HEADERS, body: JSON.stringify({ email, password, role }) })
}

export async function changeRole(id: string, role: Role): Promise<void> {
  await send(`/api/users/${id}/role`, { method: 'PUT', headers: JSON_HEADERS, body: JSON.stringify({ role }) })
}

export async function deleteUser(id: string): Promise<void> {
  await send(`/api/users/${id}`, { method: 'DELETE' })
}

// Eigenes Passwort ändern. Danach sind alle anderen Geräte abgemeldet, dieses bleibt angemeldet.
export async function changePassword(currentPassword: string, newPassword: string): Promise<void> {
  await send('/api/auth/password', {
    method: 'POST',
    headers: JSON_HEADERS,
    body: JSON.stringify({ currentPassword, newPassword }),
  })
}
