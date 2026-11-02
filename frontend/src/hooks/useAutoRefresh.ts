import { useEffect, useRef } from 'react'

// Gleicher Takt wie die Status-LEDs (StatusProvider): einmal pro Minute.
export const refreshIntervalMs = 60_000

// Ruft reload regelmäßig auf und zusätzlich sofort, wenn die App wieder sichtbar wird
// (z. B. iPhone entsperrt, Tab gewechselt). So erscheinen Werte aus dem Tuya-Hintergrund-Abgleich
// ohne Neuladen der Seite. Kostet kein Tuya-Kontingent: Die Seiten lesen nur unsere Datenbank.
//
// reload darf State setzen: Es läuft im Timer bzw. im Ereignis, nicht direkt im Effekt
// (Lint-Regel react/set-state-in-effect).
export function useAutoRefresh(reload: () => void, intervalMs: number = refreshIntervalMs) {
  // Immer die neueste Funktion merken (sie sieht z. B. den aktuellen Filter). So muss der Timer nicht
  // bei jedem Render neu gestartet werden.
  const latest = useRef(reload)
  useEffect(() => {
    latest.current = reload
  })

  useEffect(() => {
    const timer = window.setInterval(() => latest.current(), intervalMs)

    function onVisible() {
      if (document.visibilityState === 'visible') latest.current()
    }
    document.addEventListener('visibilitychange', onVisible)

    // Beim Verlassen der Seite aufräumen, sonst liefe der Timer weiter.
    return () => {
      window.clearInterval(timer)
      document.removeEventListener('visibilitychange', onVisible)
    }
  }, [intervalMs])
}
