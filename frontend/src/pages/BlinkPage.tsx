import { BlinkCameras } from '../components/BlinkCameras'

// Blink-Kameras mit ihrem letzten Vorschaubild. Nur lesend: Es wird nie ein neues Foto ausgelöst.
export function BlinkPage() {
  return (
    <section>
      <header className="page-header">
        <h1>Blink</h1>
        <p className="page-subtitle">Kameras mit ihrem letzten Vorschaubild</p>
      </header>

      <div className="camera-panel camera-panel-wide">
        <BlinkCameras />
      </div>

      <p className="page-note">
        Die Bilder holt der Server mit seiner eigenen Blink-Anmeldung und gibt nur das Bild weiter. Sie werden nicht
        gespeichert und nicht im Browser zwischengespeichert.
      </p>
    </section>
  )
}
