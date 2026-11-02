import { useEffect, useState } from 'react'
import { batteryText, formatTakenAt, getBlinkThumbnail, type BlinkCamera } from '../api/blink'
import { CameraIcon } from './Icons'
import { StatusLed } from './StatusLed'

type BlinkCameraCardProps = {
  camera: BlinkCamera
  // Ändert sich dieser Wert, wird das Bild neu geladen (z. B. nach dem Neuladen der Kameraliste).
  refreshKey: string
}

// Eine Kamera: Name, Online-LED, Akku und das letzte Vorschaubild mit "Aufnahme von …".
export function BlinkCameraCard({ camera, refreshKey }: BlinkCameraCardProps) {
  const [imageUrl, setImageUrl] = useState<string | null>(null)
  const [takenAt, setTakenAt] = useState<string | null>(camera.thumbnailTakenAtUtc)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!camera.hasThumbnail) return

    // Object-URL: eine Adresse wie "blob:http://…/1234", die nur in diesem Browser-Tab auf die Bilddaten im
    // Arbeitsspeicher zeigt. Sie muss wieder freigegeben werden, sonst bleibt das Bild im Speicher liegen.
    let objectUrl: string | null = null
    let ignore = false

    // State erst in .then()/.catch() setzen (Lint-Regel react/set-state-in-effect).
    getBlinkThumbnail(camera.id)
      .then(({ image, takenAtUtc }) => {
        if (ignore) return
        objectUrl = URL.createObjectURL(image)
        setImageUrl(objectUrl)
        setTakenAt(takenAtUtc)
        setError(null)
      })
      .catch((err: Error) => {
        if (!ignore) setError(err.message)
      })

    // Beim Verlassen der Seite oder vor dem nächsten Laden: alte Antwort ignorieren, Bild freigeben.
    return () => {
      ignore = true
      if (objectUrl !== null) URL.revokeObjectURL(objectUrl)
    }
  }, [camera.id, camera.hasThumbnail, refreshKey])

  const battery = batteryText(camera.battery)
  const onlineState = camera.online === null ? 'unknown' : camera.online ? 'online' : 'offline'
  const onlineText = camera.online === null ? 'Status unbekannt' : camera.online ? 'Online' : 'Offline'

  return (
    <article className="camera-card">
      {imageUrl !== null ? (
        <img className="camera-image" src={imageUrl} alt={`Letztes Bild der Kamera ${camera.name}`} />
      ) : (
        <div className="camera-preview">
          <CameraIcon size={20} />
          <span>{!camera.hasThumbnail ? 'Kein Bild vorhanden' : error ?? 'Bild wird geladen …'}</span>
        </div>
      )}
      <h3 className="camera-name">{camera.name}</h3>
      <p className="camera-state">
        <StatusLed state={onlineState} label={onlineText} />
        {battery !== null && <span className="camera-battery"> · {battery}</span>}
      </p>
      {camera.hasThumbnail && (
        <p className="camera-taken">{takenAt !== null ? `Aufnahme von ${formatTakenAt(takenAt)}` : 'Aufnahmezeit unbekannt'}</p>
      )}
    </article>
  )
}
