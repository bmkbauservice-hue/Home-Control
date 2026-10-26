// Blink ist noch nicht angebunden. Sobald es eine echte Verbindung gibt,
// wird der Status hier aus der API gelesen statt fest eingetragen.
export const isBlinkConnected = false

export function BlinkStatus() {
  return (
    <span className={isBlinkConnected ? 'status status-online' : 'status status-offline'}>
      <span className="status-dot" aria-hidden="true" />
      {isBlinkConnected ? 'Verbunden' : 'Noch nicht verbunden'}
    </span>
  )
}
