import { useState, type FormEvent } from 'react'
import type { Room } from '../api/rooms'
import { EditIcon, TrashIcon } from './Icons'

type RoomTileProps = {
  room: Room
  // Beide Funktionen geben true zurück, wenn die Aktion geklappt hat.
  onRename: (name: string) => Promise<boolean>
  onDelete: () => Promise<boolean>
}

// Eine Kachel kann drei Zustände haben: anzeigen, bearbeiten, Löschen bestätigen.
type Mode = 'view' | 'edit' | 'confirmDelete'

export function RoomTile({ room, onRename, onDelete }: RoomTileProps) {
  const [mode, setMode] = useState<Mode>('view')
  const [name, setName] = useState(room.name)
  const [busy, setBusy] = useState(false)

  async function handleSave(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    const ok = await onRename(name)
    setBusy(false)
    if (ok) {
      setMode('view')
    }
  }

  async function handleDelete() {
    setBusy(true)
    const ok = await onDelete()
    // Bei Erfolg verschwindet die Kachel, sonst zurück zur Anzeige.
    if (!ok) {
      setBusy(false)
      setMode('view')
    }
  }

  function cancelEdit() {
    setName(room.name)
    setMode('view')
  }

  if (mode === 'edit') {
    return (
      <form className="tile room-tile" onSubmit={handleSave}>
        <label className="field-label" htmlFor={`room-${room.id}`}>
          Raumname
        </label>
        <input
          id={`room-${room.id}`}
          className="input"
          value={name}
          maxLength={100}
          autoFocus
          onChange={(event) => setName(event.target.value)}
        />
        <div className="tile-actions">
          <button type="submit" className="button button-primary" disabled={busy}>
            Speichern
          </button>
          <button type="button" className="button" onClick={cancelEdit} disabled={busy}>
            Abbrechen
          </button>
        </div>
      </form>
    )
  }

  if (mode === 'confirmDelete') {
    return (
      <div className="tile room-tile tile-danger">
        <h3 className="tile-title">„{room.name}“ löschen?</h3>
        <p className="tile-text">{getRoomDeleteWarning(room.deviceCount)}</p>
        <div className="tile-actions">
          <button type="button" className="button button-danger" onClick={handleDelete} disabled={busy}>
            Ja, löschen
          </button>
          <button type="button" className="button" onClick={() => setMode('view')} disabled={busy}>
            Abbrechen
          </button>
        </div>
      </div>
    )
  }

  return (
    <div className="tile room-tile">
      <h3 className="tile-title">{room.name}</h3>
      <p className="tile-text">
        {room.deviceCount === 1 ? '1 Gerät' : `${room.deviceCount} Geräte`}
      </p>
      <div className="tile-actions">
        <button type="button" className="button" onClick={() => setMode('edit')}>
          <EditIcon size={20} />
          Bearbeiten
        </button>
        <button type="button" className="button button-ghost-danger" onClick={() => setMode('confirmDelete')}>
          <TrashIcon size={20} />
          Löschen
        </button>
      </div>
    </div>
  )
}

// Text für die Löschbestätigung. Beim Löschen eines Raums entfernt die Datenbank
// auch alle Geräte darin und deren gesamten Ereignisverlauf (Cascade).
function getRoomDeleteWarning(deviceCount: number): string {
  if (deviceCount === 0) {
    return 'Der Raum enthält keine Geräte. Das kann nicht rückgängig gemacht werden.'
  }
  const devices =
    deviceCount === 1
      ? 'das Gerät in diesem Raum und sein gesamter Ereignisverlauf'
      : `alle ${deviceCount} Geräte in diesem Raum und deren gesamter Ereignisverlauf`
  return `Dabei werden auch ${devices} gelöscht. Das kann nicht rückgängig gemacht werden.`
}
