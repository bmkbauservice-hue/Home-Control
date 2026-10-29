import { send } from './http'
import { getAccessToken, refreshAccessToken, setAccessToken } from './tokenStore'

// Aufrufe an die Auth-API des Backends (siehe docs/AUTHENTIFIZIERUNG.md).
// Die Typen entsprechen UserResponse und LoginResponse im Backend.

export type Role = 'Admin' | 'User'

export type AuthUser = {
  id: string
  email: string
  role: Role
}

type LoginResponse = {
  accessToken: string
  accessTokenExpiresAtUtc: string
  user: AuthUser
}

const JSON_HEADERS = { 'Content-Type': 'application/json' }

// Anmelden: Das Access Token merken wir uns, das Refresh-Token setzt der Server als Cookie.
export async function login(email: string, password: string): Promise<AuthUser> {
  const response = await send('/api/auth/login', {
    method: 'POST',
    headers: JSON_HEADERS,
    body: JSON.stringify({ email, password }),
  })
  const body = (await response.json()) as LoginResponse
  setAccessToken(body.accessToken)
  return body.user
}

// Registrieren ist nur für den allerersten Benutzer offen (wird Admin). Danach legt nur ein
// angemeldeter Admin neue Benutzer an.
export async function register(email: string, password: string): Promise<void> {
  await send('/api/auth/register', {
    method: 'POST',
    headers: JSON_HEADERS,
    body: JSON.stringify({ email, password }),
  })
}

export async function getProfile(): Promise<AuthUser> {
  const response = await send('/api/auth/profile')
  return (await response.json()) as AuthUser
}

// Abmelden: Der Server widerruft das Refresh-Token und löscht das Cookie. Lokal vergessen wir das
// Access Token auch dann, wenn der Server nicht erreichbar ist.
export async function logout(): Promise<void> {
  try {
    await send('/api/auth/logout', { method: 'POST' })
  } finally {
    setAccessToken(null)
  }
}

// Beim Start der App: Gibt es noch eine gültige Sitzung? Mit gespeichertem Access Token fragen wir
// das Profil ab (send() erneuert ein abgelaufenes Token von selbst). Ohne Token versuchen wir es
// mit dem Refresh-Cookie. Ergebnis null = bitte anmelden.
export async function restoreSession(): Promise<AuthUser | null> {
  try {
    if (getAccessToken() === null && (await refreshAccessToken()) !== 'ok') {
      return null
    }
    return await getProfile()
  } catch {
    return null
  }
}
