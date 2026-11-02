import { useEffect, useState, type FormEvent } from 'react'
import { getDevices, getDeviceTypeLabel, type Device } from '../api/devices'
import { createRoom, getRooms, type Room } from '../api/rooms'
import { useAuth } from '../auth/authContext'
import { BlinkCameras } from '../components/BlinkCameras'
import { EnergyCard } from '../components/EnergyCard'
import { ChevronRightIcon, PlusIcon, RoomsIcon } from '../components/Icons'
import { measurementsOf } from '../components/measurements'
import type { Page } from '../components/Navigation'
import { RoomIcon } from '../components/RoomIcon'
import { StatusBar } from '../components/StatusBar'
import { heroLine, overallOf } from '../components/statusText'
import { StatusLed } from '../components/StatusLed'
import { useAutoRefresh } from '../hooks/useAutoRefresh'
import { useStatus } from '../status/statusContext'

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
        // Ein früherer Fehler (z. B. kurz kein Netz) ist erledigt, sobald es wieder klappt.
        setError(null)
      })
      .catch((err: Error) => setError(err.message))
      .finally(() => setLoading(false))
  }

  useEffect(() => {
    loadData()
  }, [])

  // Jede Minute und beim Zurückkehren in die App neu laden: Der Tuya-Hintergrund-Abgleich im Backend
  // speichert neue Zustände (z. B. Tür OFFEN) auch, wenn niemand klickt.
  useAutoRefresh(loadData)

  function retry() {
    setLoading(true)
    setError(null)
    loadData()
  }

  const { status, blink, serverUnreachable, statusError } = useStatus()
  const overall = overallOf(status, blink, serverUnreachable, statusError)

  return (
    <section className="dashboard">
      {/* Begrüßung mit Aussage statt Logo: Tageszeit, Name und in einem Satz, wie es zu Hause aussieht. */}
      <header className="dashboard-hero">
        <p className="hero-date">{todayText()}</p>
        <h1 className="hero-title">
          {greeting()}, Muju.
          <span className="hero-line">{heroLine(status, overall.state)}</span>
        </h1>
      </header>

      <StatusBar />

      {error && (
        <div className="alert alert-row" role="alert">
          <span>{error}</span>
          <button type="button" className="button" onClick={retry}>
            Erneut versuchen
          </button>
        </div>
      )}

      <div className="dashboard-columns">
        <section className="dashboard-section" aria-labelledby="rooms-heading">
          <div className="section-header">
            <h2 id="rooms-heading">Räume</h2>
            {rooms.length > 0 && (
              <button type="button" className="link-button" onClick={() => onNavigate('rooms')}>
                {isAdmin ? 'Verwalten' : 'Alle Räume'}
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
          <section className="dashboard-section" aria-labelledby="energy-heading">
            <div className="section-header">
              <h2 id="energy-heading">Auf einen Blick</h2>
            </div>
            <EnergyCard devices={devices} />
          </section>

          <section className="dashboard-section" aria-labelledby="blink-heading">
            <div className="section-header">
              <h2 id="blink-heading">Kameras</h2>
              <button type="button" className="link-button" onClick={() => onNavigate('blink')}>
                Alle
              </button>
            </div>
            {/* Echte Kameras aus /api/blink/cameras mit letztem Vorschaubild (oder warum es keine gibt). */}
            <BlinkCameras />
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

// Anzeige des gespeicherten Zustands: Das Backend speichert EIN/AUS/OFFEN/ZU, gezeigt wird "An", "Aus" usw.
const stateLabels: Record<string, string> = { EIN: 'An', AUS: 'Aus', OFFEN: 'Offen', ZU: 'Zu' }

// Eine Raumkarte: Der Kopf führt zur Geräteseite mit diesem Raum als Filter. Darunter je Gerät eine Zeile:
// LED · Name und Typ · Zustand rechts (groß) mit Messwerten darunter (klein).
function RoomCard({ room, devices, onOpen }: RoomCardProps) {
  const { stateOf } = useStatus()
  const shown = devices.slice(0, devicesPerCard)
  const hidden = devices.length - shown.length
  const switchedOn = devices.filter((d) => d.lastValue === 'EIN').length
  const summary = `${devices.length === 1 ? '1 Gerät' : `${devices.length} Geräte`}${switchedOn > 0 ? ` · ${switchedOn} an` : ''}`

  return (
    <article className="room-card">
      <button type="button" className="room-card-header" onClick={onOpen}>
        <span className="room-icon">
          <RoomIcon name={room.name} />
        </span>
        <span className="room-heading">
          <span className="visually-hidden">Geräte anzeigen: </span>
          <span className="room-name">{room.name}</span>
          <span className="room-count">{summary}</span>
        </span>
        <ChevronRightIcon size={20} />
      </button>

      {devices.length === 0 ? (
        <p className="room-empty">Noch keine Geräte in diesem Raum.</p>
      ) : (
        <ul className="room-devices">
          {shown.map((device) => {
            const measurements = measurementsOf(device)
            const warning = measurements.some((m) => m.warning)
            const main = device.lastValue ? (stateLabels[device.lastValue] ?? device.lastValue) : '–'
            const sub = measurements.length > 0 ? measurements.map((m) => m.text).join(' · ') : device.lastValue ? null : 'keine Daten'
            const valueClass = [
              'room-device-value',
              device.lastValue === 'EIN' ? 'is-on' : '',
              warning ? 'value-warning' : '',
              !device.lastValue && measurements.length === 0 ? 'no-data' : '',
            ]
              .filter(Boolean)
              .join(' ')
            return (
              <li key={device.id} className="room-device">
                <StatusLed state={stateOf(device.id)} />
                <span className="room-device-text">
                  <span className="room-device-name">{device.name}</span>
                  <span className="room-device-type">{getDeviceTypeLabel(device.type)}</span>
                </span>
                <span className={valueClass}>
                  {main}
                  {sub && <small>{sub}</small>}
                </span>
              </li>
            )
          })}
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

// Datum und Uhrzeit für die Zeile über der Begrüßung, z. B. "Montag, 9. November · 18:42".
function todayText(): string {
  const now = new Date()
  const date = now.toLocaleDateString('de-DE', { weekday: 'long', day: 'numeric', month: 'long' })
  const time = now.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' })
  return `${date} · ${time}`
}

// Begrüßung passend zur Tageszeit.
function greeting(): string {
  const hour = new Date().getHours()
  if (hour < 11) return 'Guten Morgen'
  if (hour < 18) return 'Hallo'
  return 'Guten Abend'
}
