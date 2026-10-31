import { createContext, useContext } from 'react'
import type { BlinkStatusInfo, OnlineState, SystemStatus } from '../api/status'

export type StatusContextValue = {
  // Letzte Antwort von GET /api/status (null, solange noch nichts geladen ist).
  status: SystemStatus | null
  // Letzte Antwort von GET /api/blink/status (null, solange noch nichts geladen ist).
  blink: BlinkStatusInfo | null
  // true: Der Server hat beim letzten Versuch nicht geantwortet (Server-LED rot).
  serverUnreachable: boolean
  // Der Server hat geantwortet, aber mit einem Fehler (z. B. 404 bei einer alten Server-Version).
  // null, wenn die letzte Abfrage geklappt hat.
  statusError: string | null
  // true während einer Abfrage, z. B. um den Knopf "Aktualisieren" zu sperren.
  refreshing: boolean
  // Sofort neu abfragen (Knopf "Aktualisieren").
  refresh: () => void
  // LED-Zustand eines Geräts. 'unknown', solange nichts geladen ist.
  stateOf: (deviceId: string) => OnlineState
}

export const StatusContext = createContext<StatusContextValue | null>(null)

export function useStatus(): StatusContextValue {
  const value = useContext(StatusContext)
  if (value === null) {
    throw new Error('useStatus muss innerhalb von StatusProvider benutzt werden.')
  }
  return value
}
