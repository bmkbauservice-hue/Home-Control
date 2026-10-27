import { deviceTypes, isKnownDeviceType, type Device, type DeviceInput } from '../api/devices'
import type { Room } from '../api/rooms'
import type { TuyaCloudDevice } from '../api/tuya'

type DeviceFieldsProps = {
  // Macht die ids eindeutig, wenn mehrere Formulare auf der Seite sind.
  idPrefix: string
  value: DeviceInput
  rooms: Room[]
  onChange: (value: DeviceInput) => void
  // Geräte aus der Tuya-Cloud. null = Liste nicht verfügbar (Tuya nicht eingerichtet oder Fehler).
  tuyaDevices: TuyaCloudDevice[] | null
  // Alle unsere Geräte, um zu zeigen, welches Tuya-Gerät schon vergeben ist.
  devices: Device[]
  // Beim Bearbeiten das Gerät selbst. Seine eigene Verknüpfung zählt nicht als "vergeben".
  currentDeviceId?: string
}

// Die Eingabefelder für ein Gerät. Wird beim Anlegen und Bearbeiten genutzt.
export function DeviceFields({
  idPrefix,
  value,
  rooms,
  onChange,
  tuyaDevices,
  devices,
  currentDeviceId,
}: DeviceFieldsProps) {
  // Mit welchem anderen Gerät ist ein Tuya-Gerät schon verknüpft?
  function linkedElsewhere(tuyaId: string): Device | undefined {
    return devices.find((d) => d.tuyaDeviceId === tuyaId && d.id !== currentDeviceId)
  }

  // Die gespeicherte Verknüpfung bleibt sichtbar, auch wenn die Tuya-Liste sie nicht (mehr) enthält.
  const savedIdMissing =
    value.tuyaDeviceId !== '' && !(tuyaDevices ?? []).some((d) => d.tuyaId === value.tuyaDeviceId)

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

      <div className="field">
        <label className="field-label" htmlFor={`${idPrefix}-tuya`}>
          Tuya-Gerät
        </label>
        <select
          id={`${idPrefix}-tuya`}
          className="input"
          value={value.tuyaDeviceId}
          disabled={tuyaDevices === null && value.tuyaDeviceId === ''}
          onChange={(event) => onChange({ ...value, tuyaDeviceId: event.target.value })}
        >
          <option value="">Keine Verknüpfung</option>
          {savedIdMissing && <option value={value.tuyaDeviceId}>Gespeichertes Tuya-Gerät</option>}
          {(tuyaDevices ?? []).map((tuya) => {
            const linked = linkedElsewhere(tuya.tuyaId)
            return (
              <option key={tuya.tuyaId} value={tuya.tuyaId} disabled={linked !== undefined}>
                {tuya.name}
                {tuya.productName !== '' ? ` (${tuya.productName})` : ''}
                {linked ? ` – schon mit „${linked.name}“ verknüpft` : ''}
              </option>
            )
          })}
        </select>
      </div>
    </>
  )
}
