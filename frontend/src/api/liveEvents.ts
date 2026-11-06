import { send } from './http'

// Live-Kanal zum Backend (GET /api/live/devices, Server-Sent Events, docs/ECHTZEIT.md).
// Der Server meldet nur "diese Geräte haben sich geändert", die Daten holt die App danach über die normalen
// Endpunkte. Fällt der Kanal aus, läuft alles weiter wie bisher: Die Seiten laden trotzdem jede Minute neu.
//
// Warum fetch und nicht das eingebaute EventSource? EventSource kann keinen Authorization-Header schicken, das
// Token müsste dann in die URL (und damit in Logs). send() hängt das Token an und erneuert es bei Bedarf.

export type LiveState = 'connecting' | 'live' | 'offline'

const url = '/api/live/devices'
const maxRetryMs = 30_000

// Wer bei einer Änderung neu laden möchte (z. B. die offene Seite). Siehe hooks/useLiveRefresh.ts.
const listeners = new Set<() => void>()

export function subscribeDeviceChanges(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

// Ein SSE-Block besteht aus Zeilen wie "event: device-change" und "data: {...}". Wir brauchen nur den Namen:
// Die Daten holt die App ohnehin neu. Reine Funktion.
export function sseEventName(block: string): string | null {
  const line = block.split('\n').find((l) => l.startsWith('event:'))
  return line === undefined ? null : line.slice('event:'.length).trim()
}

// Liest den Strom, bis er endet. Blöcke sind durch eine Leerzeile getrennt und können über mehrere
// Netzwerk-Pakete verteilt ankommen, deshalb der Puffer.
async function readStream(body: ReadableStream<Uint8Array>, onEvent: (name: string) => void): Promise<void> {
  const reader = body.getReader()
  const decoder = new TextDecoder()
  let buffer = ''
  for (;;) {
    const { done, value } = await reader.read()
    if (done) return
    buffer += decoder.decode(value, { stream: true }).replaceAll('\r\n', '\n')
    let end = buffer.indexOf('\n\n')
    while (end !== -1) {
      const name = sseEventName(buffer.slice(0, end))
      if (name !== null) onEvent(name)
      buffer = buffer.slice(end + 2)
      end = buffer.indexOf('\n\n')
    }
  }
}

// Hält die Verbindung offen, solange der Tab sichtbar ist: Trennt sie, wenn die App in den Hintergrund geht
// (spart Akku und eine der wenigen Verbindungen des Browsers), und verbindet sich bei Fehlern mit wachsendem
// Abstand neu (1, 2, 4 … bis 30 Sekunden). Rückgabe: Funktion zum Beenden.
export function startLiveEvents(onState: (state: LiveState) => void): () => void {
  let stopped = false
  let everConnected = false
  let connection: AbortController | null = null
  let wake: (() => void) | null = null

  function sleep(ms: number): Promise<void> {
    return new Promise((resolve) => {
      const timer = window.setTimeout(resolve, ms)
      wake = () => {
        window.clearTimeout(timer)
        resolve()
      }
    })
  }

  function notifyListeners() {
    for (const listener of listeners) listener()
  }

  async function run() {
    let failures = 0
    while (!stopped) {
      if (document.visibilityState === 'hidden') {
        onState('offline')
        await sleep(maxRetryMs)
        continue
      }

      connection = new AbortController()
      onState('connecting')
      try {
        const response = await send(url, { signal: connection.signal, headers: { Accept: 'text/event-stream' } })
        if (response.body === null) throw new Error('Keine Antwort.')
        await readStream(response.body, (name) => {
          if (name === 'connected') {
            onState('live')
            failures = 0
            // Nach einer Unterbrechung könnte etwas verpasst worden sein: einmal alles neu laden.
            if (everConnected) notifyListeners()
            everConnected = true
          } else if (name === 'device-change') {
            notifyListeners()
          }
        })
        // Normales Ende (das Access Token läuft ab): gleich neu verbinden, send() holt ein neues Token.
      } catch {
        // Abbruch, Netzfehler, 503 usw.: unten mit Abstand neu versuchen.
        failures += 1
      }
      if (stopped) return
      onState('offline')
      await sleep(Math.min(1000 * 2 ** failures, maxRetryMs))
    }
  }

  function onVisibility() {
    if (document.visibilityState === 'hidden') {
      connection?.abort()
    } else {
      wake?.()
    }
  }

  document.addEventListener('visibilitychange', onVisibility)
  void run()

  return () => {
    stopped = true
    document.removeEventListener('visibilitychange', onVisibility)
    connection?.abort()
    wake?.()
  }
}
