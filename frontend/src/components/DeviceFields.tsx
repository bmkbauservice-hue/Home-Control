import { deviceTypes, isKnownDeviceType, tuyaSyncOptions, type Device, type DeviceInput } from '../api/devices'
import type { GoogleHomeDevices } from '../api/googleHome'
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
  // Von Muju Home gemeldete Geräte. null = Liste nicht verfügbar.
  googleHome: GoogleHomeDevices | null
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
  googleHome,
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

  const googleList = googleHome?.devices ?? []
  const googleSavedMissing =
    value.googleHomeDeviceId !== '' && !googleList.some((d) => d.id === value.googleHomeDeviceId)

  function linkedToGoogleElsewhere(googleId: string): Device | undefined {
    return devices.find((d) => d.googleHomeDeviceId === googleId && d.id !== currentDeviceId)
  }

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

      {/* Takt nur zeigen, wenn ein Tuya-Gerät verknüpft ist: Ohne Verknüpfung gibt es nichts abzugleichen. */}
      {value.tuyaDeviceId !== '' && (
        <div className="field">
          <label className="field-label" htmlFor={`${idPrefix}-takt`}>
            Abgleich
          </label>
          <select
            id={`${idPrefix}-takt`}
            className="input"
            value={value.tuyaSyncSeconds ?? ''}
            onChange={(event) =>
              onChange({ ...value, tuyaSyncSeconds: event.target.value === '' ? null : Number(event.target.value) })
            }
          >
            {tuyaSyncOptions.map((option) => (
              <option key={option.label} value={option.seconds ?? ''}>
                {option.label}
              </option>
            ))}
          </select>
        </div>
      )}

      <div className="field">
        <label className="field-label" htmlFor={`${idPrefix}-google`}>
          Google-Home-Gerät
        </label>
        <select
          id={`${idPrefix}-google`}
          className="input"
          value={value.googleHomeDeviceId}
          onChange={(event) => onChange({ ...value, googleHomeDeviceId: event.target.value })}
        >
          <option value="">Keine Verknüpfung</option>
          {googleSavedMissing && <option value={value.googleHomeDeviceId}>Gespeichertes Google-Home-Gerät</option>}
          {googleList.map((google) => {
            const linked = linkedToGoogleElsewhere(google.id)
            // Messpunkte und Geräte ohne Ein/Aus-Zustand lassen sich nicht auswählen.
            const blocked = linked !== undefined || google.protected || google.on === null
            return (
              <option key={google.id} value={google.id} disabled={blocked}>
                {google.name} · ID …{google.id.slice(-4)}
                {google.online ? '' : ' · offline'}
                {google.on === null ? ' · ohne Ein/Aus' : ''}
                {google.protected ? ' · Messpunkt, nie schalten' : ''}
                {linked ? ` · schon mit „${linked.name}“ verknüpft` : ''}
              </option>
            )
          })}
        </select>
        {googleHome?.connected === false && (
          <p className="field-note">Muju Home ist nicht verbunden, daher ist die Liste leer.</p>
        )}
      </div>
    </>
  )
}
