import { useEffect, useRef } from 'react'
import { subscribeDeviceChanges } from '../api/liveEvents'

// Ruft reload auf, sobald das Backend über den Live-Kanal eine Geräteänderung meldet.
// Kurz gesammelt (Debounce): Mehrere Meldungen direkt hintereinander lösen nur EIN Neuladen aus.
// Ergänzt useAutoRefresh (jede Minute), ersetzt es aber nicht: Fällt der Kanal aus, bleibt das Polling.
export function useLiveRefresh(reload: () => void, debounceMs: number = 300) {
  const latest = useRef(reload)
  useEffect(() => {
    latest.current = reload
  })

  useEffect(() => {
    let timer: number | undefined
    const unsubscribe = subscribeDeviceChanges(() => {
      window.clearTimeout(timer)
      timer = window.setTimeout(() => latest.current(), debounceMs)
    })
    return () => {
      window.clearTimeout(timer)
      unsubscribe()
    }
  }, [debounceMs])
}
