import { useCallback, useEffect, useRef, useState } from 'react'
import {
  getAlarmClips,
  getAlarmClipVideo,
  getAlarmEvents,
  type AlarmClip,
  type AlarmClips,
  type AlarmEvent,
} from '../api/alarm'
import { hasMatchingEvent, matchingClips } from '../components/alarmClips'
import { CameraIcon } from '../components/Icons'
import { ErrorMessage, LoadingMessage } from '../components/StateMessages'

const outcomeText: Record<AlarmEvent['outcome'], string> = {
  laeuft: 'Läuft',
  bestaetigt: 'Scharfschalten bestätigt',
  'nicht-bestaetigt': 'Scharfschalten nicht bestätigt',
  fehler: 'Fehler',
}

function formatTime(value: string): string {
  return new Date(value).toLocaleString('de-DE', {
    day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit',
  })
}

export function AlarmPage() {
  const [events, setEvents] = useState<AlarmEvent[] | null>(null)
  const [eventsError, setEventsError] = useState<string | null>(null)
  const [clips, setClips] = useState<AlarmClips | null>(null)
  const [clipsError, setClipsError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  // Standbilder aus bereits abgespielten Clips. Sie bleiben nur in dieser Ansicht, gespeichert wird nichts.
  const [previews, setPreviews] = useState<Record<string, string>>({})

  const loadEvents = useCallback(() => {
    getAlarmEvents()
      .then((loaded) => {
        setEvents(loaded)
        setEventsError(null)
      })
      .catch((err: Error) => setEventsError(err.message))
  }, [])

  useEffect(() => {
    loadEvents()
    // Die Clip-Liste dauert länger. Die Ereignisse erscheinen deshalb unabhängig davon sofort.
    getAlarmClips()
      .then(setClips)
      .catch((err: Error) => setClipsError(err.message))
    const timer = window.setInterval(loadEvents, 15000)
    return () => window.clearInterval(timer)
  }, [loadEvents])

  function refreshClips() {
    setRefreshing(true)
    setClipsError(null)
    getAlarmClips(true)
      .then(setClips)
      .catch((err: Error) => setClipsError(err.message))
      .finally(() => setRefreshing(false))
  }

  const addPreview = useCallback((key: string, image: string) => {
    setPreviews((current) => ({ ...current, [key]: image }))
  }, [])

  const clipList = clips?.clips ?? []

  return (
    <section className="alarm-page">
      <h1>Alarm</h1>

      <section className="alarm-section" aria-labelledby="alarm-events-heading">
        <h2 id="alarm-events-heading">Verlauf</h2>
        {eventsError && <ErrorMessage message={eventsError} onRetry={loadEvents} />}
        {events === null && eventsError === null && <LoadingMessage text="Verlauf wird geladen …" />}
        {events?.length === 0 && <p className="empty">Noch keine Ereignisse. Sie erscheinen, sobald eine Regel ausgelöst hat.</p>}
        <ul className="alarm-events">
          {events?.map((event) => (
            <EventRow key={event.id} event={event} clips={clipList} clipsLoaded={clips !== null} />
          ))}
        </ul>
      </section>

      <section className="alarm-section" aria-labelledby="alarm-clips-heading">
        <div className="section-header">
          <h2 id="alarm-clips-heading">Clips vom USB-Stick</h2>
          <button type="button" className="link-button" onClick={refreshClips} disabled={refreshing}>
            {refreshing ? 'Lädt …' : 'Liste aktualisieren'}
          </button>
        </div>
        {clipsError && <p className="alert" role="alert">{clipsError}</p>}
        {clips?.problem && <p className="field-note">{clips.problem}</p>}
        {clips === null && clipsError === null && <LoadingMessage text="Das Sync Module stellt die Liste bereit …" />}
        {clips !== null && clipList.length === 0 && !clips.problem && <p className="empty">Auf dem USB-Stick liegen keine Clips.</p>}
        <div className="alarm-clips">
          {clipList.map((clip) => (
            <ClipCard
              key={clip.key}
              clip={clip}
              preview={previews[clip.key]}
              timeMatch={hasMatchingEvent(clip.createdAtUtc, events ?? [])}
              onPreview={addPreview}
            />
          ))}
        </div>
      </section>
    </section>
  )
}

type EventRowProps = { event: AlarmEvent; clips: AlarmClip[]; clipsLoaded: boolean }

function EventRow({ event, clips, clipsLoaded }: EventRowProps) {
  const matching = matchingClips(event.startedAtUtc, clips)
  return (
    <li className="alarm-event">
      <div className="alarm-event-head">
        <strong>{event.trigger === 'tuer' ? `${event.doorName} ging auf` : 'Regel von Hand getestet'}</strong>
        <time dateTime={event.startedAtUtc}>{formatTime(event.startedAtUtc)}</time>
      </div>
      <p className={`alarm-outcome alarm-outcome-${event.outcome}`}>
        {event.blinkSystemName}: {outcomeText[event.outcome]}
      </p>
      <small>{event.message}</small>
      {matching.length > 0 ? (
        <div className="alarm-matches">
          {matching.map((clip) => (
            <a key={clip.key} className="alarm-match" href={`#clip-${clip.key}`}>
              Zeitlich passend · {clip.cameraName} · {formatTime(clip.createdAtUtc)}
            </a>
          ))}
        </div>
      ) : (
        clipsLoaded && <small>Noch kein zeitlich passender Clip.</small>
      )}
    </li>
  )
}

type ClipCardProps = {
  clip: AlarmClip
  preview: string | undefined
  timeMatch: boolean
  onPreview: (key: string, image: string) => void
}

function ClipCard({ clip, preview, timeMatch, onPreview }: ClipCardProps) {
  const [videoUrl, setVideoUrl] = useState<string | null>(null)
  // Nach dem Ende zeigt die Karte wieder das Standbild. Das geladene Video bleibt für „Abspielen“ erhalten.
  const [playing, setPlaying] = useState(false)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const videoRef = useRef<HTMLVideoElement | null>(null)
  const objectUrl = useRef<string | null>(null)

  useEffect(() => () => {
    if (objectUrl.current !== null) URL.revokeObjectURL(objectUrl.current)
  }, [])

  // Das Video wird erst hier geladen, also erst nach dem Klick.
  async function play() {
    if (videoUrl !== null) {
      setPlaying(true)
      return
    }
    setLoading(true)
    setError(null)
    try {
      const url = URL.createObjectURL(await getAlarmClipVideo(clip.key))
      objectUrl.current = url
      setVideoUrl(url)
      setPlaying(true)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setLoading(false)
    }
  }

  // Sobald ein Bild da ist, dient es als Vorschau. Der Browser darf den Ton-Autostart ablehnen, dann bleibt die Play-Taste.
  function handleLoaded() {
    const video = videoRef.current
    if (video === null) return
    void video.play().catch(() => undefined)
    if (preview !== undefined || video.videoWidth === 0) return
    const canvas = document.createElement('canvas')
    canvas.width = 480
    canvas.height = Math.round((480 * video.videoHeight) / video.videoWidth)
    canvas.getContext('2d')?.drawImage(video, 0, 0, canvas.width, canvas.height)
    onPreview(clip.key, canvas.toDataURL('image/jpeg', 0.7))
  }

  return (
    <article className="alarm-clip" id={`clip-${clip.key}`}>
      {playing && videoUrl !== null ? (
        <video ref={videoRef} className="alarm-video" src={videoUrl} controls playsInline
          onLoadedData={handleLoaded} onEnded={() => setPlaying(false)} />
      ) : (
        <div className="alarm-placeholder">
          {preview !== undefined ? <img src={preview} alt={`Standbild aus dem Clip von ${clip.cameraName}`} /> : <CameraIcon size={32} />}
          <button type="button" className="button button-primary" onClick={() => void play()} disabled={loading}>
            {loading ? 'Clip wird geladen …' : 'Abspielen'}
          </button>
        </div>
      )}
      <div className="alarm-clip-text">
        <strong>{clip.cameraName}</strong>
        <time dateTime={clip.createdAtUtc}>{formatTime(clip.createdAtUtc)}</time>
        {timeMatch && <span className="alarm-time-match">Zeitlich passend</span>}
        {error && <p className="alert" role="alert">{error}</p>}
      </div>
    </article>
  )
}
