import { useEffect, useState, type FormEvent } from 'react'
import { getDevices, getDeviceTypeLabel, type Device } from '../api/devices'
import { createRoom, getRooms, type Room } from '../api/rooms'
import { useAuth } from '../auth/authContext'
import { BlinkStatus } from '../components/BlinkStatus'
import { DeviceTypeIcon } from '../components/DeviceTypeIcon'
import { CameraIcon, ChevronRightIcon, DeviceIcon, HomeIcon, PlusIcon, RoomsIcon } from '../components/Icons'
import type { Page } from '../components/Navigation'
import { RoomIcon } from '../components/RoomIcon'
import { TuyaLightCard } from '../components/TuyaLightCard'

// Zwei Plätze für die Blink-Kameras. Echte Namen und Bilder kommen erst mit der Blink-Anbindung.
const cameraSlots = ['Kamera 1', 'Kamera 2']

// So viele Geräte zeigt eine Raumkarte, der Rest wird als "+ N weitere" zusammengefasst.
const devicesPerCard = 4

type DashboardPageProps = {
  onNavigate: (page: Page) => void
  // Öffnet die Geräteseite, gefiltert auf diesen Raum.
  onOpenRoom: (roomId: string) => void
}

export function DashboardPage({ onNavigate, onOpenRoom }: DashboardPageProps) {
  const { isAdmin } = useAuth()
  const [rooms, setRooms] = useState<Room[]>([])
  const [devices, setDevices] = useState<Device[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Räume und Geräte gleichzeitig laden. Die Geräte braucht man für die Raumkarten.
  function loadData() {
    return Promise.all([getRooms(), getDevices()])
      .then(([loadedRooms, loadedDevices]) => {
        setRooms(loadedRooms)
        setDevices(loadedDevices)
      })
      .catch((err: Error) => setError(err.message))
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    loadData()
  }, [])

  function retry() {
    setLoading(true)
    setError(null)
    loadData()
  }

  // Was in den Übersichtskarten steht, solange noch nichts geladen ist.
  let roomValue = String(rooms.length)
  let deviceValue = String(devices.length)
  if (loading) {
    roomValue = '…'
    deviceValue = '…'
  } else if (error) {
    roomValue = '–'
    deviceValue = '–'
  }

  return (
    <section className="dashboard">
      <header className="dashboard-hero">
        <div className="hero-brand">
          <span className="hero-logo">
            <HomeIcon size={34} />
          </span>
          <h1 className="hero-title">Home Control</h1>
        </div>
        <p className="hero-greeting">Hallo Muju</p>
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

      <div className="stat-grid">
        <button type="button" className="stat-card stat-link" onClick={() => onNavigate('rooms')}>
          <span className="stat-icon">
            <RoomsIcon size={22} />
          </span>
          <span className="stat-text">
            <span className="stat-label">Räume</span>
            <span className="stat-value">{roomValue}</span>
          </span>
          <ChevronRightIcon size={20} />
        </button>

        <button type="button" className="stat-card stat-link" onClick={() => onNavigate('devices')}>
          <span className="stat-icon">
            <DeviceIcon size={22} />
          </span>
          <span className="stat-text">
            <span className="stat-label">Geräte</span>
            <span className="stat-value">{deviceValue}</span>
          </span>
          <ChevronRightIcon size={20} />
        </button>

        <button type="button" className="stat-card stat-link" onClick={() => onNavigate('blink')}>
          <span className="stat-icon">
            <CameraIcon size={22} />
          </span>
          <span className="stat-text">
            <span className="stat-label">Kameraverbindung</span>
            <BlinkStatus />
          </span>
          <ChevronRightIcon size={20} />
        </button>
      </div>

      <div className="dashboard-columns">
        <section className="dashboard-section" aria-labelledby="rooms-heading">
          <div className="section-header">
            <h2 id="rooms-heading">Räume</h2>
            {rooms.length > 0 && isAdmin && (
              <button type="button" className="button" onClick={() => onNavigate('rooms')}>
                Verwalten
              </button>
            )}
          </div>

          {loading && <p className="empty">Räume werden geladen …</p>}

          {!loading && error && <p className="empty">Räume konnten nicht geladen werden.</p>}

          {!loading && !error && rooms.length === 0 && (
            <EmptyRooms canCreate={isAdmin} onCreated={(room) => setRooms([room])} />
          )}

          {!loading && rooms.length > 0 && (
            <div className="room-grid">
              {rooms.map((room) => (
                <RoomCard
                  key={room.id}
                  room={room}
                  devices={devices.filter((d) => d.roomId === room.id)}
                  onOpen={() => onOpenRoom(room.id)}
                />
              ))}
            </div>
          )}
        </section>

        <div className="dashboard-side">
          <section className="dashboard-section" aria-labelledby="light-heading">
            <div className="section-header">
              <h2 id="light-heading">Licht (Tuya)</h2>
            </div>

            <TuyaLightCard />
          </section>

          <section className="dashboard-section" aria-labelledby="blink-heading">
            <div className="section-header">
              <h2 id="blink-heading">Kameras (Blink)</h2>
            </div>

            <div className="camera-panel">
              <BlinkStatus />
              <div className="camera-grid">
                {cameraSlots.map((name) => (
                  <article key={name} className="camera-card">
                    {/* Bewusst kein Bild: Ohne Verbindung gibt es noch keine Aufnahmen. */}
                    <div className="camera-preview">
                      <CameraIcon size={20} />
                      <span>Kein Kamerabild</span>
                    </div>
                    <h3 className="camera-name">{name}</h3>
                    <p className="camera-state">Noch nicht verbunden</p>
                  </article>
                ))}
              </div>
            </div>
          </section>
        </div>
      </div>
    </section>
  )
}

type RoomCardProps = {
  room: Room
  devices: Device[]
  onOpen: () => void
}

// Eine Raumkarte: Der Kopf führt zur Geräteseite mit diesem Raum als Filter.
// Darunter stehen die Geräte mit Typ und ehrlich gekennzeichnetem (fehlendem) Messwert.
function RoomCard({ room, devices, onOpen }: RoomCardProps) {
  const shown = devices.slice(0, devicesPerCard)
  const hidden = devices.length - shown.length

  return (
    <article className="room-card">
      <button type="button" className="room-card-header" onClick={onOpen}>
        <span className="room-icon">
          <RoomIcon name={room.name} />
        </span>
        <span className="room-heading">
          <span className="visually-hidden">Geräte anzeigen: </span>
          <span className="room-name">{room.name}</span>
          <span className="room-count">{devices.length === 1 ? '1 Gerät' : `${devices.length} Geräte`}</span>
        </span>
        <ChevronRightIcon size={20} />
      </button>

      {devices.length === 0 ? (
        <p className="room-empty">Noch keine Geräte in diesem Raum.</p>
      ) : (
        <ul className="room-devices">
          {shown.map((device) => (
            <li key={device.id} className="room-device">
              <span className="room-device-icon">
                <DeviceTypeIcon type={device.type} />
              </span>
              <span className="room-device-text">
                <span className="room-device-name" title={device.name}>
                  {device.name}
                </span>
                <span className="room-device-type">{getDeviceTypeLabel(device.type)}</span>
                {/* Ehrlich: Ohne Geräteanbindung gibt es noch keine Messwerte. */}
                <span className={device.lastValue ? 'room-device-value' : 'room-device-value no-data'}>
                  {device.lastValue ?? 'Noch keine Daten'}
                </span>
              </span>
            </li>
          ))}
          {hidden > 0 && <li className="room-device-more">+ {hidden} weitere</li>}
        </ul>
      )}
    </article>
  )
}

type EmptyRoomsProps = {
  // Nur Admins dürfen Räume anlegen.
  canCreate: boolean
  onCreated: (room: Room) => void
}

// Leerzustand, wenn noch kein Raum existiert. Der Button öffnet ein kleines Formular.
function EmptyRooms({ canCreate, onCreated }: EmptyRoomsProps) {
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
        {canCreate
          ? 'Lege deinen ersten Raum an, zum Beispiel Wohnzimmer oder Küche. Er erscheint dann hier als Kachel.'
          : 'Ein Admin kann Räume anlegen. Sie erscheinen dann hier als Kacheln.'}
      </p>

      {!canCreate ? null : showForm ? (
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
