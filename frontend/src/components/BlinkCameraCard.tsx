import { useEffect, useState } from 'react'
import {
  batteryText,
  formatTakenAt,
  getBlinkThumbnail,
  isOldThumbnail,
  probeLiveView,
  requestNewThumbnail,
  type BlinkCamera,
} from '../api/blink'
import { CameraIcon } from './Icons'
import { StatusLed } from './StatusLed'

type BlinkCameraCardProps = {
  camera: BlinkCamera
  // Ändert sich dieser Wert, wird das Bild neu geladen (z. B. nach dem Neuladen der Kameraliste).
  refreshKey: string
  // Nur Admins sehen den Knopf "Neues Bild" (das Backend prüft es trotzdem selbst).
  canRequestNewImage?: boolean
}

// Eine Kamera: Name, Online-LED, Akku und das letzte Vorschaubild mit "Aufnahme von …".
export function BlinkCameraCard({ camera, refreshKey, canRequestNewImage = false }: BlinkCameraCardProps) {
  const [imageUrl, setImageUrl] = useState<string | null>(null)
  const [takenAt, setTakenAt] = useState<string | null>(camera.thumbnailTakenAtUtc)
  const [error, setError] = useState<string | null>(null)
  // Zählt die "Neues Bild"-Aufrufe; ändert er sich, lädt der Effekt unten das Bild neu.
  const [snapshotCount, setSnapshotCount] = useState(0)
  const [busy, setBusy] = useState(false)
  const [snapshotMessage, setSnapshotMessage] = useState<string | null>(null)
  const [probing, setProbing] = useState(false)
  const [probeMessage, setProbeMessage] = useState<string | null>(null)

  async function handleNewImage() {
    setBusy(true)
    setSnapshotMessage(null)
    try {
      const result = await requestNewThumbnail(camera.id)
      if (!result.completed) {
        setSnapshotMessage('Blink hat das Bild noch nicht fertig gemeldet. In einer Minute erneut versuchen.')
      }
      setSnapshotCount((count) => count + 1)
    } catch (err) {
      // Z. B. 429: "Neues Bild für diese Kamera höchstens einmal pro Minute. …"
      setSnapshotMessage((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  // Bewusst kein "Live"-Etikett: Die Probe zählt nur Videodaten, es läuft kein Videobild.
  async function handleLiveProbe() {
    setProbing(true)
    setProbeMessage(null)
    try {
      const result = await probeLiveView(camera.id)
      setProbeMessage(result.videoReceived
        ? `Probe erfolgreich: ${result.videoFrames} Videopakete empfangen. Die Wiedergabe ist noch nicht eingebaut.`
        : `Kein Video empfangen: ${result.problem ?? 'unbekannter Grund'}`)
    } catch (err) {
      setProbeMessage((err as Error).message)
    } finally {
      setProbing(false)
    }
  }

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
  }, [camera.id, camera.hasThumbnail, refreshKey, snapshotCount])

  const battery = batteryText(camera.battery)
  const onlineState = camera.online === null ? 'unknown' : camera.online ? 'online' : 'offline'
  const onlineText = camera.online === null ? 'Status unbekannt' : camera.online ? 'Online' : 'Offline'
  const old = takenAt !== null && isOldThumbnail(takenAt)

  return (
    <article className="camera-card">
      {/* Bild mit Etikett (Online · Akku) oben links, darunter Name und Aufnahmezeit. */}
      <figure className="camera-figure">
        {imageUrl !== null ? (
          <img className="camera-image" src={imageUrl} alt={`Letztes Bild der Kamera ${camera.name}`} />
        ) : (
          <div className="camera-preview">
            <CameraIcon size={20} />
            <span>{!camera.hasThumbnail ? 'Kein Bild vorhanden' : error ?? 'Bild wird geladen …'}</span>
          </div>
        )}
        <span className="camera-tag">
          <StatusLed state={onlineState} label={battery !== null ? `${onlineText} · ${battery}` : onlineText} />
        </span>
        <figcaption className="camera-caption">
          <h3 className="camera-name">{camera.name}</h3>
          {camera.hasThumbnail && (
            <p className={old ? 'camera-taken camera-taken-old' : 'camera-taken'}>
              {takenAt !== null ? `Aufnahme von ${formatTakenAt(takenAt)}` : 'Aufnahmezeit unbekannt'}
            </p>
          )}
        </figcaption>
      </figure>
      {canRequestNewImage && (
        <div className="camera-actions">
          <button type="button" className="button" onClick={handleNewImage} disabled={busy}>
            {busy ? 'Kamera nimmt auf …' : 'Neues Bild'}
          </button>
          <button type="button" className="button" onClick={handleLiveProbe} disabled={probing}>
            {probing ? 'Prüfe …' : 'Livebild prüfen'}
          </button>
          <span className="camera-hint">kostet Akku, höchstens 1× pro Minute</span>
          {probeMessage !== null && (
            <p className="camera-snapshot-message" role="status">
              {probeMessage}
            </p>
          )}
          {snapshotMessage !== null && (
            <p className="camera-snapshot-message" role="status">
              {snapshotMessage}
            </p>
          )}
        </div>
      )}
    </article>
  )
}
