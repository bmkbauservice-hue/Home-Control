import { send } from './http'

// Läuft diese Instanz im Demo-Modus? (GET /api/demo, ohne Anmeldung, entspricht DemoInfoResponse im Backend)
export type DemoInfo = {
  demoMode: boolean
  // Nur im Demo-Modus: das Demo-Konto, damit die Anmeldeseite es vorausfüllen kann. Das Passwort steht im Terminal.
  demoEmail: string | null
}

const noDemo: DemoInfo = { demoMode: false, demoEmail: null }

// Die Antwort ändert sich während der Laufzeit nicht. Deshalb nur EINE Anfrage, alle Aufrufer teilen das Ergebnis.
let cached: Promise<DemoInfo> | null = null

export function getDemoInfo(): Promise<DemoInfo> {
  cached ??= send('/api/demo')
    .then((response) => response.json() as Promise<DemoInfo>)
    .catch(() => {
      // Backend nicht erreichbar: kein Banner, beim nächsten Aufruf neu fragen.
      cached = null
      return noDemo
    })
  return cached
}
