import { useState, type FormEvent } from 'react'
import { deviceToInput, getDeviceTypeLabel, type Device, type DeviceInput } from '../api/devices'
import type { Room } from '../api/rooms'
import type { TuyaCloudDevice } from '../api/tuya'
import { DeviceEvents } from './DeviceEvents'
import { DeviceFields } from './DeviceFields'
import { DeviceTypeIcon } from './DeviceTypeIcon'
import { EditIcon, HistoryIcon, TrashIcon } from './Icons'
import { measuredAtText, measurementsOf, stateLabel } from './measurements'
import { StatusLed } from './StatusLed'
import { useStatus } from '../status/statusContext'

type DeviceTileProps = {
  device: Device
  rooms: Room[]
  // Für die Auswahl des Tuya-Geräts beim Bearbeiten (siehe DeviceFields)
  tuyaDevices: TuyaCloudDevice[] | null
  devices: Device[]
  // Bearbeiten, Löschen und manuelle Testeinträge nur für Admins.
  canEdit: boolean
  // Beide Funktionen geben true zurück, wenn die Aktion geklappt hat.
  onSave: (input: DeviceInput) => Promise<boolean>
  onDelete: () => Promise<boolean>
}

// Wie bei RoomTile: anzeigen, bearbeiten oder Löschen bestätigen – zusätzlich den Verlauf zeigen.
type Mode = 'view' | 'edit' | 'confirmDelete' | 'events'

export function DeviceTile({ device, rooms, tuyaDevices, devices, canEdit, onSave, onDelete }: DeviceTileProps) {
  const [mode, setMode] = useState<Mode>('view')
  const [input, setInput] = useState<DeviceInput>(deviceToInput(device))
  const [busy, setBusy] = useState(false)
  const { stateOf } = useStatus()
  const onlineState = stateOf(device.id)
  const measurements = measurementsOf(device)

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
    setInput(deviceToInput(device))
    setMode('view')
  }

  if (mode === 'edit') {
    return (
      <form className="tile device-tile" onSubmit={handleSave}>
        <DeviceFields
          idPrefix={`device-${device.id}`}
          value={input}
          rooms={rooms}
          onChange={setInput}
          tuyaDevices={tuyaDevices}
          devices={devices}
          currentDeviceId={device.id}
        />
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
        <p className="tile-text">
          Dabei wird auch der gesamte Ereignisverlauf dieses Geräts gelöscht. Das kann nicht rückgängig
          gemacht werden.
        </p>
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
        <DeviceEvents deviceId={device.id} canAdd={canEdit} />
      </div>
    )
  }

  return (
    <div className="tile device-tile">
      {/* Kopf wie bei den Räumen: Symbol des Gerätetyps, Name, Typ. Die LED steht als eigene Zeile darunter,
          damit auch ein langer Name die volle Breite hat und nichts überlappt. */}
      <div className="tile-head">
        <span className="room-icon">
          <DeviceTypeIcon type={device.type} />
        </span>
        <div className="tile-head-text">
          <h3 className="tile-title">{device.name}</h3>
          <p className="tile-text">{getDeviceTypeLabel(device.type)}</p>
          <span className="device-led">
            {onlineState === 'unknown' ? (
              <StatusLed state="unknown" label="ohne Status" title="Kein Online-Status (nicht mit Tuya verknüpft oder Tuya meldet nichts)" />
            ) : (
              <StatusLed state={onlineState} label={onlineState} />
            )}
          </span>
        </div>
      </div>
      {/* Zustand groß, Messwerte darunter (feste Ziffernbreite, siehe .device-state) */}
      <p className={device.lastValue === 'EIN' ? 'device-state is-on' : device.lastValue ? 'device-state' : 'device-state no-data'}>
        {device.lastValue ? stateLabel(device.lastValue) : 'Noch keine Daten'}
        {measurements.length > 0 && (
          <small>
            {measurements.map((m, index) => (
              <span key={m.text} className={m.warning ? 'value-warning' : undefined}>
                {index > 0 && ' · '}
                {m.text}
              </span>
            ))}
          </small>
        )}
      </p>
      <dl className="tile-values">
        {measurements.length > 0 && (
          <>
            <dt>Gemessen</dt>
            <dd>
              {measuredAtText(device.measuredAtUtc) ?? 'unbekannt'}
              {/* Offline: Tuya kennt nur den alten Stand, das soll man sehen. */}
              {onlineState === 'offline' && ' (Gerät offline, letzter Stand)'}
            </dd>
          </>
        )}
        {device.tuyaDeviceId && (
          <>
            <dt>Tuya</dt>
            <dd>{tuyaNameFor(device.tuyaDeviceId, tuyaDevices)}</dd>
          </>
        )}
      </dl>
      <div className="tile-actions tile-actions-row">
        <button type="button" className="button" onClick={() => setMode('events')}>
          <HistoryIcon size={20} />
          Ereignisse
        </button>
        {canEdit && (
          <>
            {/* Nur Symbole, damit alle Knöpfe in eine Zeile passen. aria-label/title für Screenreader und Maus. */}
            <button
              type="button"
              className="button button-icon"
              onClick={() => setMode('edit')}
              aria-label={`${device.name} bearbeiten`}
              title="Bearbeiten"
            >
              <EditIcon size={20} />
            </button>
            <button
              type="button"
              className="button button-ghost-danger button-icon"
              onClick={() => setMode('confirmDelete')}
              aria-label={`${device.name} löschen`}
              title="Löschen"
            >
              <TrashIcon size={20} />
            </button>
          </>
        )}
      </div>
    </div>
  )
}

// Name des verknüpften Tuya-Geräts. Ohne Tuya-Liste steht nur "verknüpft" da (die Id selbst nicht).
function tuyaNameFor(tuyaId: string, tuyaDevices: TuyaCloudDevice[] | null): string {
  return tuyaDevices?.find((d) => d.tuyaId === tuyaId)?.name ?? 'verknüpft'
}
