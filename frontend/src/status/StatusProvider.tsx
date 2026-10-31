import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { getBlinkStatus, getStatus, type BlinkStatusInfo, type OnlineState, type SystemStatus } from '../api/status'
import { StatusContext, type StatusContextValue } from './statusContext'

// Alle 60 Sekunden neu fragen. Das Backend merkt sich die Tuya-Antwort ebenfalls 60 Sekunden,
// es entstehen also höchstens ein Tuya-Aufruf pro Minute, egal wie viele Geräte die App offen haben.
const refreshIntervalMs = 60_000

// Fragt den Status regelmäßig ab und stellt ihn der ganzen App bereit (Context API), damit
// Status-Leiste, Raumkarten und Geräte-Kacheln dieselben Daten benutzen und nicht jede Komponente
// selbst den Server fragt.
export function StatusProvider({ children }: { children: ReactNode }) {
  const [status, setStatus] = useState<SystemStatus | null>(null)
  const [blink, setBlink] = useState<BlinkStatusInfo | null>(null)
  const [serverUnreachable, setServerUnreachable] = useState(false)
  const [refreshing, setRefreshing] = useState(false)
  // Verhindert, dass nach dem Verlassen der App noch ein State gesetzt wird.
  const active = useRef(true)

  // State wird erst in .then()/.catch() gesetzt, also wenn die Antwort da ist, nicht schon beim
  // Aufruf. So löst der Aufruf im useEffect keinen sofortigen zweiten Render aus (Lint-Regel
  // react/set-state-in-effect). "refreshing" setzt nur der Knopf (refresh unten).
  const loadStatus = useCallback(
    () =>
      getStatus()
        .then((next) => {
          if (!active.current) return
          setStatus(next)
          setServerUnreachable(false)
        })
        .catch(() => {
          if (active.current) setServerUnreachable(true)
        })
        .finally(() => {
          if (active.current) setRefreshing(false)
        }),
    [],
  )

  // Blink nur beim Start und auf Knopfdruck, nicht jede Minute: Blink hat keine offizielle API,
  // und jeder Abruf dort soll selten bleiben (siehe docs/BLINK.md).
  const loadBlink = useCallback(
    () =>
      getBlinkStatus()
        .then((next) => {
          if (active.current) setBlink(next)
        })
        .catch(() => {
          // Ohne Antwort bleibt die Blink-LED grau. Die Server-LED zeigt schon, ob der Server fehlt.
        }),
    [],
  )

  useEffect(() => {
    active.current = true
    loadStatus()
    loadBlink()
    const timer = window.setInterval(loadStatus, refreshIntervalMs)

    // Kommt die App aus dem Hintergrund zurück (z. B. iPhone entsperrt), gleich neu fragen.
    function onVisible() {
      if (document.visibilityState === 'visible') loadStatus()
    }
    document.addEventListener('visibilitychange', onVisible)

    return () => {
      active.current = false
      window.clearInterval(timer)
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [loadStatus, loadBlink])

  // Knopf "Aktualisieren": Hier ist setState erlaubt, denn es ist ein Klick (Ereignis), kein Effekt.
  const refresh = useCallback(() => {
    setRefreshing(true)
    loadStatus()
    loadBlink()
  }, [loadStatus, loadBlink])

  const value = useMemo<StatusContextValue>(() => {
    // Nachschlagetabelle Geräte-Id → Zustand, einmal pro neuer Antwort gebaut.
    const byId = new Map<string, OnlineState>(status?.devices.map((d) => [d.id, d.state]) ?? [])
    return {
      status,
      blink,
      serverUnreachable,
      refreshing,
      refresh,
      stateOf: (deviceId: string) => byId.get(deviceId) ?? 'unknown',
    }
  }, [status, blink, serverUnreachable, refreshing, refresh])

  return <StatusContext.Provider value={value}>{children}</StatusContext.Provider>
}
