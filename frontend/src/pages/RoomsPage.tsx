import { useEffect, useState, type FormEvent } from 'react'
import { createRoom, deleteRoom, getRooms, updateRoom, type Room } from '../api/rooms'
import { PlusIcon } from '../components/Icons'
import { RoomTile } from '../components/RoomTile'

export function RoomsPage() {
  const [rooms, setRooms] = useState<Room[]>([])
  const [search, setSearch] = useState('')
  const [newName, setNewName] = useState('')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Räume laden – beim ersten Anzeigen und bei jeder Änderung der Suche.
  useEffect(() => {
    // ignore = true, sobald eine neuere Suche läuft. Dann wird eine ältere,
    // langsamere Antwort verworfen und überschreibt das neuere Ergebnis nicht.
    let ignore = false

    // Beim Tippen kurz warten, damit nicht jeder Tastendruck eine Anfrage auslöst.
    const delay = search.trim() === '' ? 0 : 300
    const timer = setTimeout(() => {
      getRooms(search)
        .then((loaded) => {
          if (!ignore) setRooms(loaded)
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
  }, [search])

  function changeSearch(value: string) {
    setSearch(value)
    setLoading(true)
    setError(null)
  }

  const isSearching = search.trim() !== ''

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
          {loading
            ? 'Wird geladen …'
            : `${rooms.length} ${rooms.length === 1 ? 'Raum' : 'Räume'}${isSearching ? ' gefunden' : ''}`}
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

      <div className="tile filter-bar">
        <div className="field">
          <label className="field-label" htmlFor="room-search">
            Räume durchsuchen
          </label>
          <input
            id="room-search"
            type="search"
            className="input"
            placeholder="Name enthält …"
            value={search}
            onChange={(event) => changeSearch(event.target.value)}
          />
        </div>
        <button type="button" className="button" onClick={() => changeSearch('')} disabled={!isSearching}>
          Suche zurücksetzen
        </button>
      </div>

      {error && (
        <p className="alert" role="alert">
          {error}
        </p>
      )}

      {!loading && rooms.length === 0 && !error && !isSearching && (
        <p className="empty">Noch keine Räume. Lege oben deinen ersten Raum an.</p>
      )}

      {!loading && rooms.length === 0 && !error && isSearching && (
        <p className="empty">Keine passenden Räume gefunden.</p>
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
