import { deviceTypes, isKnownDeviceType, type DeviceInput } from '../api/devices'
import type { Room } from '../api/rooms'

type DeviceFieldsProps = {
  // Macht die ids eindeutig, wenn mehrere Formulare auf der Seite sind.
  idPrefix: string
  value: DeviceInput
  rooms: Room[]
  onChange: (value: DeviceInput) => void
}

// Die drei Eingabefelder für ein Gerät. Wird beim Anlegen und Bearbeiten genutzt.
export function DeviceFields({ idPrefix, value, rooms, onChange }: DeviceFieldsProps) {
  return (
    <>
      <div className="field">
        <label className="field-label" htmlFor={`${idPrefix}-name`}>
          Name
        </label>
        <input
          id={`${idPrefix}-name`}
          className="input"
          placeholder="z. B. Stehlampe"
          value={value.name}
          maxLength={100}
          onChange={(event) => onChange({ ...value, name: event.target.value })}
        />
      </div>

      <div className="field">
        <label className="field-label" htmlFor={`${idPrefix}-type`}>
          Typ
        </label>
        <select
          id={`${idPrefix}-type`}
          className="input"
          value={value.type}
          onChange={(event) => onChange({ ...value, type: event.target.value })}
        >
          {/* Ein älterer, gespeicherter Typ bleibt auswählbar, damit er nicht still ersetzt wird. */}
          {value.type !== '' && !isKnownDeviceType(value.type) && (
            <option value={value.type}>{value.type} (bisheriger Typ)</option>
          )}
          {deviceTypes.map((type) => (
            <option key={type.value} value={type.value}>
              {type.label}
            </option>
          ))}
        </select>
      </div>

      <div className="field">
        <label className="field-label" htmlFor={`${idPrefix}-room`}>
          Raum
        </label>
        <select
          id={`${idPrefix}-room`}
          className="input"
          value={value.roomId}
          onChange={(event) => onChange({ ...value, roomId: event.target.value })}
        >
          {rooms.map((room) => (
            <option key={room.id} value={room.id}>
              {room.name}
            </option>
          ))}
        </select>
      </div>
    </>
  )
}
