import { useEffect, useState, type FormEvent } from 'react'
import {
  createDevice,
  deleteDevice,
  deviceTypes,
  emptyDeviceFilter,
  getDevices,
  updateDevice,
  type Device,
  type DeviceFilter,
  type DeviceInput,
} from '../api/devices'
import { getRooms, type Room } from '../api/rooms'
import { getTuyaDevices, type TuyaCloudDevice } from '../api/tuya'
import { DeviceFields } from '../components/DeviceFields'
import { DeviceTile } from '../components/DeviceTile'
import { PlusIcon } from '../components/Icons'
import type { Page } from '../components/Navigation'

type DevicesPageProps = {
  onNavigate: (page: Page) => void
  // Optional: Raum, nach dem beim Öffnen gefiltert wird (z. B. von einer Raumkarte der Übersicht).
  initialRoomId?: string
}

export function DevicesPage({ onNavigate, initialRoomId = '' }: DevicesPageProps) {
  const [devices, setDevices] = useState<Device[]>([])
  const [rooms, setRooms] = useState<Room[]>([])
  const [filter, setFilter] = useState<DeviceFilter>({ ...emptyDeviceFilter, roomId: initialRoomId })
  const [newDevice, setNewDevice] = useState<DeviceInput>({ name: '', type: 'Light', roomId: '', tuyaDeviceId: '' })
  // Geräte aus der Tuya-Cloud für die Auswahl. null = (noch) nicht verfügbar.
  const [tuyaDevices, setTuyaDevices] = useState<TuyaCloudDevice[] | null>(null)
  const [tuyaError, setTuyaError] = useState<string | null>(null)
  const [roomsLoading, setRoomsLoading] = useState(true)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Räume einmal laden. Man braucht sie für das Formular, den Raumfilter und die Gruppen.
  useEffect(() => {
    getRooms()
      .then((loadedRooms) => {
        setRooms(loadedRooms)
        // Den ersten Raum vorauswählen, damit das Formular sofort benutzbar ist.
        setNewDevice((current) => ({ ...current, roomId: loadedRooms[0]?.id ?? '' }))
      })
      .catch((err: Error) => setError(err.message))
      .finally(() => setRoomsLoading(false))
  }, [])

  // Tuya-Geräte einmal laden. Tuya ist optional: Ein Fehler hier blockiert die Seite nicht,
  // sondern erscheint nur als Hinweis beim Formular.
  useEffect(() => {
    let ignore = false
    getTuyaDevices()
      .then((loaded) => {
        if (!ignore) setTuyaDevices(loaded)
      })
      .catch((err: Error) => {
        if (!ignore) setTuyaError(err.message)
      })
    return () => {
      ignore = true
    }
  }, [])

  // Geräte laden – beim ersten Anzeigen und bei jeder Änderung von Suche oder Filter.
  useEffect(() => {
    // ignore = true, sobald eine neuere Anfrage läuft. Dann wird eine ältere,
    // langsamere Antwort verworfen und überschreibt das neuere Ergebnis nicht.
    let ignore = false

    // Beim Tippen kurz warten, damit nicht jeder Tastendruck eine Anfrage auslöst.
    const delay = filter.q.trim() === '' ? 0 : 300
    const timer = setTimeout(() => {
      getDevices(filter)
        .then((loaded) => {
          if (!ignore) setDevices(loaded)
        })
        .catch((err: Error) => {
          if (!ignore) setError(err.message)
        })
        .finally(() => {
          if (!ignore) setLoading(false)
        })
    }, delay)

    return () => {
      ignore = true
      clearTimeout(timer)
    }
  }, [filter])

  function changeFilter(next: DeviceFilter) {
    setFilter(next)
    setLoading(true)
    setError(null)
  }

  const hasFilter = filter.q.trim() !== '' || filter.roomId !== '' || filter.type !== ''

  async function handleCreate(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const device = await createDevice(newDevice)
      setDevices((current) => sortByName([...current, device]))
      // Typ und Raum bleiben stehen, so lassen sich mehrere Geräte schnell nacheinander anlegen.
      // Die Tuya-Verknüpfung wird geleert, denn jedes Tuya-Gerät gehört nur zu einem Gerät.
      setNewDevice((current) => ({ ...current, name: '', tuyaDeviceId: '' }))
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setSaving(false)
    }
  }

  async function handleSave(id: string, input: DeviceInput): Promise<boolean> {
    setError(null)
    try {
      const updated = await updateDevice(id, input)
      setDevices((current) => sortByName(current.map((d) => (d.id === id ? updated : d))))
      return true
    } catch (err) {
      setError((err as Error).message)
      return false
    }
  }

  async function handleDelete(id: string): Promise<boolean> {
    setError(null)
    try {
      await deleteDevice(id)
      setDevices((current) => current.filter((d) => d.id !== id))
      return true
    } catch (err) {
      setError((err as Error).message)
      return false
    }
  }

  // Nur Räume, in denen auch Geräte sind, bekommen eine eigene Gruppe.
  const groups = rooms
    .map((room) => ({ room, devices: devices.filter((d) => d.roomId === room.id) }))
    .filter((group) => group.devices.length > 0)

  return (
    <section>
      <header className="page-header">
        <h1>Geräte</h1>
        <p className="page-subtitle">
          {loading
            ? 'Wird geladen …'
            : `${devices.length} ${devices.length === 1 ? 'Gerät' : 'Geräte'}${hasFilter ? ' gefunden' : ''}`}
        </p>
      </header>

      {error && (
        <p className="alert" role="alert">
          {error}
        </p>
      )}

      {!roomsLoading && !error && rooms.length === 0 && (
        <div className="empty-state">
          <h2 className="tile-title">Zuerst einen Raum anlegen</h2>
          <p className="tile-text">Jedes Gerät gehört zu einem Raum. Lege deshalb zuerst einen Raum an.</p>
          <button type="button" className="button button-primary" onClick={() => onNavigate('rooms')}>
            Zu den Räumen
          </button>
        </div>
      )}

      {rooms.length > 0 && (
        <form className="tile create-form" onSubmit={handleCreate}>
          <h2 className="field-label">Neues Gerät anlegen</h2>
          <div className="form-grid">
            <DeviceFields
              idPrefix="new-device"
              value={newDevice}
              rooms={rooms}
              onChange={setNewDevice}
              tuyaDevices={tuyaDevices}
              devices={devices}
            />
            <button
              type="submit"
              className="button button-primary"
              disabled={saving || newDevice.name.trim() === ''}
            >
              <PlusIcon size={20} />
              Anlegen
            </button>
          </div>
          {tuyaError && <p className="field-note">Tuya-Geräteliste nicht verfügbar: {tuyaError}</p>}
        </form>
      )}

      {rooms.length > 0 && (
        <div className="tile filter-bar">
          <div className="field">
            <label className="field-label" htmlFor="device-search">
              Suche
            </label>
            <input
              id="device-search"
              type="search"
              className="input"
              placeholder="Name enthält …"
              value={filter.q}
              onChange={(event) => changeFilter({ ...filter, q: event.target.value })}
            />
          </div>

          <div className="field">
            <label className="field-label" htmlFor="device-filter-room">
              Raum
            </label>
            <select
              id="device-filter-room"
              className="input"
              value={filter.roomId}
              onChange={(event) => changeFilter({ ...filter, roomId: event.target.value })}
            >
              <option value="">Alle</option>
              {rooms.map((room) => (
                <option key={room.id} value={room.id}>
                  {room.name}
                </option>
              ))}
            </select>
          </div>

          <div className="field">
            <label className="field-label" htmlFor="device-filter-type">
              Gerätetyp
            </label>
            <select
              id="device-filter-type"
              className="input"
              value={filter.type}
              onChange={(event) => changeFilter({ ...filter, type: event.target.value })}
            >
              <option value="">Alle</option>
              {deviceTypes.map((type) => (
                <option key={type.value} value={type.value}>
                  {type.label}
                </option>
              ))}
            </select>
          </div>

          <button
            type="button"
            className="button"
            onClick={() => changeFilter(emptyDeviceFilter)}
            disabled={!hasFilter}
          >
            Filter zurücksetzen
          </button>
        </div>
      )}

      {!loading && !error && rooms.length > 0 && devices.length === 0 && !hasFilter && (
        <p className="empty">Noch keine Geräte. Lege oben dein erstes Gerät an.</p>
      )}

      {!loading && !error && rooms.length > 0 && devices.length === 0 && hasFilter && (
        <p className="empty">Keine passenden Geräte gefunden.</p>
      )}

      {groups.map((group) => (
        <section key={group.room.id} className="device-group" aria-label={group.room.name}>
          <h2 className="group-title">{group.room.name}</h2>
          <div className="tile-grid">
            {group.devices.map((device) => (
              <DeviceTile
                key={device.id}
                device={device}
                rooms={rooms}
                tuyaDevices={tuyaDevices}
                devices={devices}
                onSave={(input) => handleSave(device.id, input)}
                onDelete={() => handleDelete(device.id)}
              />
            ))}
          </div>
        </section>
      ))}
    </section>
  )
}

function sortByName(devices: Device[]): Device[] {
  return [...devices].sort((a, b) => a.name.localeCompare(b.name, 'de'))
}
