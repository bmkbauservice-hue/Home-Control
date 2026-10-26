import { useEffect, useState, type FormEvent } from 'react'
import { createDevice, deleteDevice, getDevices, updateDevice, type Device, type DeviceInput } from '../api/devices'
import { getRooms, type Room } from '../api/rooms'
import { DeviceFields } from '../components/DeviceFields'
import { DeviceTile } from '../components/DeviceTile'
import { PlusIcon } from '../components/Icons'
import type { Page } from '../components/Navigation'

type DevicesPageProps = {
  onNavigate: (page: Page) => void
}

export function DevicesPage({ onNavigate }: DevicesPageProps) {
  const [devices, setDevices] = useState<Device[]>([])
  const [rooms, setRooms] = useState<Room[]>([])
  const [newDevice, setNewDevice] = useState<DeviceInput>({ name: '', type: 'Light', roomId: '' })
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Geräte und Räume gleichzeitig laden. Die Räume braucht man für die Auswahl im Formular.
  useEffect(() => {
    Promise.all([getDevices(), getRooms()])
      .then(([loadedDevices, loadedRooms]) => {
        setDevices(loadedDevices)
        setRooms(loadedRooms)
        // Den ersten Raum vorauswählen, damit das Formular sofort benutzbar ist.
        setNewDevice((current) => ({ ...current, roomId: loadedRooms[0]?.id ?? '' }))
      })
      .catch((err: Error) => setError(err.message))
      .finally(() => setLoading(false))
  }, [])

  async function handleCreate(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const device = await createDevice(newDevice)
      setDevices((current) => sortByName([...current, device]))
      // Typ und Raum bleiben stehen, so lassen sich mehrere Geräte schnell nacheinander anlegen.
      setNewDevice((current) => ({ ...current, name: '' }))
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
          {loading ? 'Wird geladen …' : `${devices.length} ${devices.length === 1 ? 'Gerät' : 'Geräte'}`}
        </p>
      </header>

      {error && (
        <p className="alert" role="alert">
          {error}
        </p>
      )}

      {!loading && !error && rooms.length === 0 && (
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
            <DeviceFields idPrefix="new-device" value={newDevice} rooms={rooms} onChange={setNewDevice} />
            <button
              type="submit"
              className="button button-primary"
              disabled={saving || newDevice.name.trim() === ''}
            >
              <PlusIcon size={20} />
              Anlegen
            </button>
          </div>
        </form>
      )}

      {!loading && rooms.length > 0 && devices.length === 0 && (
        <p className="empty">Noch keine Geräte. Lege oben dein erstes Gerät an.</p>
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
