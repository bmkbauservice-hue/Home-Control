import { createContext, useContext } from 'react'
import type { AuthUser } from '../api/auth'

// loading: Beim Start wird noch geprüft, ob eine Sitzung besteht.
export type AuthStatus = 'loading' | 'anonymous' | 'authenticated'

export type AuthContextValue = {
  status: AuthStatus
  user: AuthUser | null
  // Nur Admins dürfen Räume und Geräte anlegen, ändern und löschen (das Backend prüft es ebenfalls).
  isAdmin: boolean
  signIn: (email: string, password: string) => Promise<void>
  // Legt das erste Konto an (wird Admin) und meldet gleich an.
  setUpFirstAccount: (email: string, password: string) => Promise<void>
  signOut: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext)
  if (value === null) {
    throw new Error('useAuth muss innerhalb von AuthProvider benutzt werden.')
  }
  return value
}
