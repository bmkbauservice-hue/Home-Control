import { BlinkStatus } from '../components/BlinkStatus'
import { CameraIcon } from '../components/Icons'

// Platzhalter-Seite: Die Anbindung an Blink folgt in einem späteren Schritt.
export function BlinkPage() {
  return (
    <section>
      <header className="page-header">
        <h1>Blink</h1>
        <p className="page-subtitle">Kameras und Bewegungsmelder</p>
      </header>

      <div className="tile blink-card">
        <span className="tile-icon">
          <CameraIcon size={40} />
        </span>
        <BlinkStatus />
        <p className="tile-text">
          Die Verbindung zu Blink ist vorbereitet, aber noch nicht eingerichtet. Sobald sie steht,
          erscheinen hier deine Kameras.
        </p>
        <button type="button" className="button" disabled>
          Verbinden (folgt)
        </button>
      </div>
    </section>
  )
}
