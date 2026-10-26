import { useState, type FormEvent } from 'react'
import { getDeviceTypeLabel, type Device, type DeviceInput } from '../api/devices'
import type { Room } from '../api/rooms'
import { DeviceEvents } from './DeviceEvents'
import { DeviceFields } from './DeviceFields'
import { DeviceIcon, EditIcon, HistoryIcon, TrashIcon } from './Icons'

type DeviceTileProps = {
  device: Device
  rooms: Room[]
  // Beide Funktionen geben true zurück, wenn die Aktion geklappt hat.
  onSave: (input: DeviceInput) => Promise<boolean>
  onDelete: () => Promise<boolean>
}

// Wie bei RoomTile: anzeigen, bearbeiten oder Löschen bestätigen – zusätzlich den Verlauf zeigen.
type Mode = 'view' | 'edit' | 'confirmDelete' | 'events'

export function DeviceTile({ device, rooms, onSave, onDelete }: DeviceTileProps) {
  const [mode, setMode] = useState<Mode>('view')
  const [input, setInput] = useState<DeviceInput>(toInput(device))
  const [busy, setBusy] = useState(false)

  async function handleSave(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    const ok = await onSave(input)
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
    setInput(toInput(device))
    setMode('view')
  }

  if (mode === 'edit') {
    return (
      <form className="tile device-tile" onSubmit={handleSave}>
        <DeviceFields idPrefix={`device-${device.id}`} value={input} rooms={rooms} onChange={setInput} />
        <div className="tile-actions">
          <button type="submit" className="button button-primary" disabled={busy || input.name.trim() === ''}>
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
      <div className="tile device-tile tile-danger">
        <h3 className="tile-title">„{device.name}“ löschen?</h3>
        <p className="tile-text">Das kann nicht rückgängig gemacht werden.</p>
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

  if (mode === 'events') {
    // Der Verlauf braucht Platz, deshalb nimmt die Kachel die ganze Zeile ein.
    return (
      <div className="tile device-tile device-tile-wide">
        <div className="events-header">
          <div>
            <h3 className="tile-title">{device.name}</h3>
            <p className="tile-text">{getDeviceTypeLabel(device.type)} · Ereignisse</p>
          </div>
          <button type="button" className="button" onClick={() => setMode('view')}>
            Schließen
          </button>
        </div>
        <DeviceEvents deviceId={device.id} />
      </div>
    )
  }

  return (
    <div className="tile device-tile">
      <span className="tile-icon">
        <DeviceIcon size={28} />
      </span>
      <h3 className="tile-title">{device.name}</h3>
      <p className="tile-text">{getDeviceTypeLabel(device.type)}</p>
      <dl className="tile-values">
        <dt>Zustand</dt>
        <dd>{device.lastValue ?? 'Noch keine Daten'}</dd>
      </dl>
      <div className="tile-actions">
        <button type="button" className="button button-full" onClick={() => setMode('events')}>
          <HistoryIcon size={20} />
          Ereignisse
        </button>
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

function toInput(device: Device): DeviceInput {
  return { name: device.name, type: device.type, roomId: device.roomId }
}
