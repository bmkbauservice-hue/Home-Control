import { useEffect, useState } from 'react'
import { getDemoInfo } from '../api/demo'

// true, wenn das Backend im Demo-Modus läuft (Banner, Begrüßung ohne echten Namen). Bis die Antwort da ist: false.
// getDemoInfo() fragt nur einmal, egal wie viele Komponenten diesen Hook benutzen.
export function useDemoMode(): boolean {
  const [demoMode, setDemoMode] = useState(false)

  useEffect(() => {
    let ignore = false
    // State erst in .then() setzen (Lint-Regel react/set-state-in-effect).
    getDemoInfo().then((info) => {
      if (!ignore) setDemoMode(info.demoMode)
    })
    return () => {
      ignore = true
    }
  }, [])

  return demoMode
}
