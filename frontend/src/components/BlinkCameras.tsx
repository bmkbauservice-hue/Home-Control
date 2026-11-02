import { useEffect, useState } from 'react'
import { getBlinkCameras, type BlinkCamera } from '../api/blink'
import { useAutoRefresh } from '../hooks/useAutoRefresh'
import { useStatus } from '../status/statusContext'
import { BlinkCameraCard } from './BlinkCameraCard'
import { BlinkStatus } from './BlinkStatus'

// Liste der Blink-Kameras mit Vorschaubild. Lädt nur, wenn Blink verbunden ist; sonst steht dort ehrlich,
// warum nicht (z. B. "Blink in der Entwicklung aus"). Jede Minute neu, das Backend hält Bilder 60 s vor.
export function BlinkCameras() {
  const { blink } = useStatus()
  const connected = blink?.state === 'connected'
  const [cameras, setCameras] = useState<BlinkCamera[] | null>(null)
  const [loadedAt, setLoadedAt] = useState('')
  const [error, setError] = useState<string | null>(null)

  function load() {
    getBlinkCameras()
      .then((loaded) => {
        setCameras(loaded)
        setLoadedAt(new Date().toISOString())
        setError(null)
      })
      .catch((err: Error) => setError(err.message))
  }

  useEffect(() => {
    if (connected) load()
  }, [connected])

  useAutoRefresh(() => {
    if (connected) load()
  })

  if (!connected) {
    return <BlinkStatus />
  }

  if (error !== null && cameras === null) {
    return <p className="camera-state">Blink: derzeit nicht verfügbar ({error})</p>
  }

  if (cameras === null) {
    return <p className="camera-state">Kameras werden geladen …</p>
  }

  if (cameras.length === 0) {
    return <p className="camera-state">Blink ist verbunden, aber es wurden keine Kameras gefunden.</p>
  }

  return (
    <div className="camera-grid">
      {cameras.map((camera) => (
        // Neues Bild laden, wenn Blink einen neuen Aufnahmezeitpunkt meldet; ohne Zeitpunkt bei jedem Neuladen.
        <BlinkCameraCard key={camera.id} camera={camera} refreshKey={camera.thumbnailTakenAtUtc ?? loadedAt} />
      ))}
    </div>
  )
}
