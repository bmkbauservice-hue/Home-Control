import { useEffect, useState, type FormEvent } from 'react'
import { createRoom, deleteRoom, getRooms, updateRoom, type Room } from '../api/rooms'
import { PlusIcon } from '../components/Icons'
import { RoomTile } from '../components/RoomTile'

export function RoomsPage() {
  const [rooms, setRooms] = useState<Room[]>([])
  const [newName, setNewName] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Beim ersten Anzeigen der Seite alle Räume laden.
  useEffect(() => {
    getRooms()
      .then(setRooms)
      .catch((err: Error) => setError(err.message))
      .finally(() => setLoading(false))
  }, [])

  async function handleCreate(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const room = await createRoom(newName)
      setRooms((current) => sortByName([...current, room]))
      setNewName('')
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setSaving(false)
    }
  }

  async function handleRename(id: string, name: string): Promise<boolean> {
    setError(null)
    try {
      const updated = await updateRoom(id, name)
      setRooms((current) => sortByName(current.map((r) => (r.id === id ? updated : r))))
      return true
    } catch (err) {
      setError((err as Error).message)
      return false
    }
  }

  async function handleDelete(id: string): Promise<boolean> {
    setError(null)
    try {
      await deleteRoom(id)
      setRooms((current) => current.filter((r) => r.id !== id))
      return true
    } catch (err) {
      setError((err as Error).message)
      return false
    }
  }

  return (
    <section>
      <header className="page-header">
        <h1>Räume</h1>
        <p className="page-subtitle">
          {loading ? 'Wird geladen …' : `${rooms.length} ${rooms.length === 1 ? 'Raum' : 'Räume'}`}
        </p>
      </header>

      <form className="tile create-form" onSubmit={handleCreate}>
        <label className="field-label" htmlFor="new-room">
          Neuen Raum anlegen
        </label>
        <div className="create-row">
          <input
            id="new-room"
            className="input"
            placeholder="z. B. Wohnzimmer"
            value={newName}
            maxLength={100}
            onChange={(event) => setNewName(event.target.value)}
          />
          <button type="submit" className="button button-primary" disabled={saving || newName.trim() === ''}>
            <PlusIcon size={20} />
            Anlegen
          </button>
        </div>
      </form>

      {error && (
        <p className="alert" role="alert">
          {error}
        </p>
      )}

      {!loading && rooms.length === 0 && !error && (
        <p className="empty">Noch keine Räume. Lege oben deinen ersten Raum an.</p>
      )}

      <div className="tile-grid">
        {rooms.map((room) => (
          <RoomTile
            // Der Name im key sorgt dafür, dass die Kachel nach dem Speichern neu startet.
            key={`${room.id}-${room.name}`}
            room={room}
            onRename={(name) => handleRename(room.id, name)}
            onDelete={() => handleDelete(room.id)}
          />
        ))}
      </div>
    </section>
  )
}

function sortByName(rooms: Room[]): Room[] {
  return [...rooms].sort((a, b) => a.name.localeCompare(b.name, 'de'))
}
