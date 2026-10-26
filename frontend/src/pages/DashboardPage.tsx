import { useEffect, useState, type FormEvent } from 'react'
import { createRoom, getRooms, type Room } from '../api/rooms'
import { BlinkStatus } from '../components/BlinkStatus'
import { CameraIcon, DeviceIcon, PlusIcon, RoomsIcon } from '../components/Icons'
import type { Page } from '../components/Navigation'

// Zwei Plätze für die Blink-Kameras. Echte Namen und Bilder kommen erst mit der Blink-Anbindung.
const cameraSlots = ['Kamera 1', 'Kamera 2']

type DashboardPageProps = {
  onNavigate: (page: Page) => void
}

export function DashboardPage({ onNavigate }: DashboardPageProps) {
  const [rooms, setRooms] = useState<Room[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  function loadRooms() {
    return getRooms()
      .then(setRooms)
      .catch((err: Error) => setError(err.message))
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    loadRooms()
  }, [])

  function retry() {
    setLoading(true)
    setError(null)
    loadRooms()
  }

  // Die Geräteanzahl kommt aus der API (deviceCount pro Raum).
  const deviceCount = rooms.reduce((sum, room) => sum + room.deviceCount, 0)

  // Was in den Übersichtskarten steht, solange noch nichts geladen ist.
  let roomValue = String(rooms.length)
  let deviceValue = String(deviceCount)
  if (loading) {
    roomValue = '…'
    deviceValue = '…'
  } else if (error) {
    roomValue = '–'
    deviceValue = '–'
  }

  return (
    <section className="dashboard">
      <header className="dashboard-header">
        <p className="eyebrow">Home Control</p>
        <h1>Hallo Muju</h1>
        <p className="page-subtitle">Dein Zuhause auf einen Blick</p>
      </header>

      {error && (
        <div className="alert alert-row" role="alert">
          <span>{error}</span>
          <button type="button" className="button" onClick={retry}>
            Erneut versuchen
          </button>
        </div>
      )}

      <div className="summary-grid">
        <button type="button" className="tile tile-link summary-card" onClick={() => onNavigate('rooms')}>
          <span className="tile-icon">
            <RoomsIcon size={28} />
          </span>
          <span className="summary-label">Räume</span>
          <span className="summary-value">{roomValue}</span>
          <span className="tile-text">Zur Raumverwaltung</span>
        </button>

        <button type="button" className="tile tile-link summary-card" onClick={() => onNavigate('devices')}>
          <span className="tile-icon">
            <DeviceIcon size={28} />
          </span>
          <span className="summary-label">Geräte</span>
          <span className="summary-value">{deviceValue}</span>
          <span className="tile-text">Status: Noch keine Daten</span>
        </button>

        <button type="button" className="tile tile-link summary-card" onClick={() => onNavigate('blink')}>
          <span className="tile-icon">
            <CameraIcon size={28} />
          </span>
          <span className="summary-label">Kameraverbindung</span>
          <BlinkStatus />
          <span className="tile-text">Blink · Platz für {cameraSlots.length} Kameras</span>
        </button>
      </div>

      <div className="dashboard-columns">
        <section className="dashboard-section" aria-labelledby="rooms-heading">
          <div className="section-header">
            <h2 id="rooms-heading">Räume</h2>
            {rooms.length > 0 && (
              <button type="button" className="button" onClick={() => onNavigate('rooms')}>
                Verwalten
              </button>
            )}
          </div>

          {loading && <p className="empty">Räume werden geladen …</p>}

          {!loading && error && <p className="empty">Räume konnten nicht geladen werden.</p>}

          {!loading && !error && rooms.length === 0 && (
            <EmptyRooms onCreated={(room) => setRooms([room])} />
          )}

          {!loading && rooms.length > 0 && (
            <div className="room-grid">
              {rooms.map((room) => (
                <article key={room.id} className="tile room-card">
                  <span className="tile-icon">
                    <RoomsIcon size={28} />
                  </span>
                  <h3 className="tile-title">{room.name}</h3>
                  <p className="tile-text">
                    {room.deviceCount === 1 ? '1 Gerät' : `${room.deviceCount} Geräte`}
                  </p>
                  <dl className="tile-values">
                    <dt>Messwerte</dt>
                    <dd>Noch keine Daten</dd>
                  </dl>
                </article>
              ))}
            </div>
          )}
        </section>

        <section className="dashboard-section" aria-labelledby="blink-heading">
          <div className="section-header">
            <h2 id="blink-heading">Blink-Kameras</h2>
            <BlinkStatus />
          </div>

          <div className="camera-grid">
            {cameraSlots.map((name) => (
              <article key={name} className="tile camera-card">
                {/* Bewusst kein Bild: Ohne Verbindung gibt es noch keine Aufnahmen. */}
                <div className="camera-preview">
                  <CameraIcon size={36} />
                  <span>Kein Kamerabild</span>
                </div>
                <h3 className="tile-title">{name}</h3>
                <p className="tile-text">Noch nicht verbunden</p>
              </article>
            ))}
          </div>
        </section>
      </div>
    </section>
  )
}

type EmptyRoomsProps = {
  onCreated: (room: Room) => void
}

// Leerzustand, wenn noch kein Raum existiert. Der Button öffnet ein kleines Formular.
function EmptyRooms({ onCreated }: EmptyRoomsProps) {
  const [showForm, setShowForm] = useState(false)
  const [name, setName] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      onCreated(await createRoom(name))
    } catch (err) {
      setError((err as Error).message)
      setSaving(false)
    }
  }

  return (
    <div className="empty-state">
      <span className="tile-icon">
        <RoomsIcon size={32} />
      </span>
      <h3 className="tile-title">Noch keine Räume</h3>
      <p className="tile-text">
        Lege deinen ersten Raum an, zum Beispiel Wohnzimmer oder Küche. Er erscheint dann hier als
        Kachel.
      </p>

      {showForm ? (
        <form className="create-row empty-form" onSubmit={handleSubmit}>
          <label className="visually-hidden" htmlFor="first-room">
            Name des Raums
          </label>
          <input
            id="first-room"
            className="input"
            placeholder="z. B. Wohnzimmer"
            value={name}
            maxLength={100}
            autoFocus
            onChange={(event) => setName(event.target.value)}
          />
          <button type="submit" className="button button-primary" disabled={saving || name.trim() === ''}>
            Anlegen
          </button>
          <button type="button" className="button" onClick={() => setShowForm(false)} disabled={saving}>
            Abbrechen
          </button>
        </form>
      ) : (
        <button type="button" className="button button-primary" onClick={() => setShowForm(true)}>
          <PlusIcon size={20} />
          Ersten Raum anlegen
        </button>
      )}

      {error && (
        <p className="alert" role="alert">
          {error}
        </p>
      )}
    </div>
  )
}
