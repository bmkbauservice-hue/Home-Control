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
import { getTuyaDevices, syncLinkedDevices, type TuyaCloudDevice } from '../api/tuya'
import { useAuth } from '../auth/authContext'
import { DeviceFields } from '../components/DeviceFields'
import { DeviceTile } from '../components/DeviceTile'
import { PlusIcon } from '../components/Icons'
import type { Page } from '../components/Navigation'
import { ErrorMessage, LoadingMessage } from '../components/StateMessages'
import { useAutoRefresh } from '../hooks/useAutoRefresh'

type DevicesPageProps = {
  onNavigate: (page: Page) => void
  // Optional: Raum, nach dem beim Öffnen gefiltert wird (z. B. von einer Raumkarte der Übersicht).
  initialRoomId?: string
}

export function DevicesPage({ onNavigate, initialRoomId = '' }: DevicesPageProps) {
  const { isAdmin } = useAuth()
  const [devices, setDevices] = useState<Device[]>([])
  const [rooms, setRooms] = useState<Room[]>([])
  const [filter, setFilter] = useState<DeviceFilter>({ ...emptyDeviceFilter, roomId: initialRoomId })
  const [newDevice, setNewDevice] = useState<DeviceInput>({
    name: '',
    type: 'Light',
    roomId: '',
    tuyaDeviceId: '',
    tuyaSyncSeconds: null,
  })
  // Geräte aus der Tuya-Cloud für die Auswahl. null = (noch) nicht verfügbar.
  const [tuyaDevices, setTuyaDevices] = useState<TuyaCloudDevice[] | null>(null)
  const [tuyaError, setTuyaError] = useState<string | null>(null)
  const [roomsLoading, setRoomsLoading] = useState(true)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  // error: Speichern, Löschen oder Abgleich fehlgeschlagen. loadError: Laden fehlgeschlagen (mit "Erneut versuchen").
  const [error, setError] = useState<string | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  // Abgleich mit Tuya: läuft gerade? Ergebnis als kurzer Text.
  const [syncing, setSyncing] = useState(false)
  const [syncMessage, setSyncMessage] = useState<string | null>(null)
  // Wird hochgezählt, um die Geräteliste neu zu laden (nach dem Abgleich und regelmäßig, siehe unten).
  const [reloadKey, setReloadKey] = useState(0)

  // Jede Minute und beim Zurückkehren in die App die Geräteliste neu laden (mit dem aktuellen Filter):
  // Der Tuya-Hintergrund-Abgleich speichert neue Werte auch ohne Klick. Ein offenes Bearbeiten-Formular
  // bleibt dabei erhalten, sein Inhalt steht im State der DeviceTile.
  useAutoRefresh(() => setReloadKey((key) => key + 1))

  // Räume laden. Man braucht sie für das Formular, den Raumfilter und die Gruppen.
  function loadRooms() {
    getRooms()
      .then((loadedRooms) => {
        setRooms(loadedRooms)
        // Den ersten Raum vorauswählen, damit das Formular sofort benutzbar ist.
        setNewDevice((current) => ({ ...current, roomId: current.roomId || (loadedRooms[0]?.id ?? '') }))
      })
      .catch((err: Error) => setLoadError(err.message))
      .finally(() => setRoomsLoading(false))
  }

  // Einmal beim ersten Anzeigen.
  useEffect(() => {
    loadRooms()
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
        .then(() => {
          // Ein früherer Ladefehler (z. B. kurz kein Netz) ist erledigt, sobald es wieder klappt.
          if (!ignore) setLoadError(null)
        })
        .catch((err: Error) => {
          if (!ignore) setLoadError(err.message)
        })
        .finally(() => {
          if (!ignore) setLoading(false)
        })
    }, delay)

    return () => {
      ignore = true
      clearTimeout(timer)
    }
  }, [filter, reloadKey])

  function changeFilter(next: DeviceFilter) {
    setFilter(next)
    setLoading(true)
    setError(null)
  }

  // "Erneut versuchen": Räume und Geräte noch einmal laden.
  function retry() {
    setLoadError(null)
    setLoading(true)
    loadRooms()
    setReloadKey((key) => key + 1)
  }

  const hasFilter = filter.q.trim() !== '' || filter.roomId !== '' || filter.type !== ''

  // Liest alle verknüpften Geräte bei Tuya und lädt danach die Liste neu.
  async function handleSync() {
    setSyncing(true)
    setError(null)
    setSyncMessage(null)
    try {
      const result = await syncLinkedDevices()
      const changed = result.devices.filter((d) => d.syncResult === 'changed').length
      const failed = result.devices.filter((d) => d.error !== null).length
      setSyncMessage(
        `${result.devices.length} verknüpfte Geräte abgeglichen, ${changed} geändert` +
          (failed > 0 ? `, ${failed} ohne Antwort von Tuya` : '') +
          '.',
      )
      setReloadKey((key) => key + 1)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setSyncing(false)
    }
  }

  async function handleCreate(event: FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const device = await createDevice(newDevice)
      setDevices((current) => sortByName([...current, device]))
      // Typ und Raum bleiben stehen, so lassen sich mehrere Geräte schnell nacheinander anlegen.
      // Die Tuya-Verknüpfung wird geleert, denn jedes Tuya-Gerät gehört nur zu einem Gerät.
      setNewDevice((current) => ({ ...current, name: '', tuyaDeviceId: '', tuyaSyncSeconds: null }))
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

      {error && <ErrorMessage message={error} />}

      {loadError && <ErrorMessage message={loadError} onRetry={retry} />}

      {/* Schlanke Leiste: Erklärung bzw. Ergebnis links, Knopf rechts (auf dem Handy untereinander) */}
      <div className="tile sync-bar">
        <p className="sync-text">
          {syncMessage ??
            'Sofort alle verknüpften Tuya-Geräte lesen (sonst automatisch im Hintergrund, Takt pro Gerät unter Einstellungen). Es wird nichts geschaltet.'}
        </p>
        <button type="button" className="button" onClick={handleSync} disabled={syncing}>
          {syncing ? 'Gleiche ab …' : 'Mit Tuya abgleichen'}
        </button>
      </div>

      {!roomsLoading && !error && !loadError && rooms.length === 0 && (
        <div className="empty-state">
          <h2 className="tile-title">Zuerst einen Raum anlegen</h2>
          <p className="tile-text">
            {isAdmin
              ? 'Jedes Gerät gehört zu einem Raum. Lege deshalb zuerst einen Raum an.'
              : 'Jedes Gerät gehört zu einem Raum. Ein Admin muss zuerst einen Raum anlegen.'}
          </p>
          {isAdmin && (
            <button type="button" className="button button-primary" onClick={() => onNavigate('rooms')}>
              Zu den Räumen
            </button>
          )}
        </div>
      )}

      {isAdmin && rooms.length > 0 && (
        // Aufklappbar (<details>): Das lange Formular verdrängt sonst auf dem Handy die Geräteliste.
        // Eingebaute HTML-Funktion, ohne eigenes JavaScript, mit Tastatur und Screenreader bedienbar.
        <details className="tile create-details">
          <summary className="create-summary">
            <PlusIcon size={20} />
            Neues Gerät anlegen
          </summary>
          <form className="create-form" onSubmit={handleCreate}>
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
        </details>
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

      {loading && devices.length === 0 && <LoadingMessage text="Geräte werden geladen …" />}

      {!loading && !error && !loadError && rooms.length > 0 && devices.length === 0 && !hasFilter && (
        <p className="empty">
          {isAdmin ? 'Noch keine Geräte. Lege oben dein erstes Gerät an.' : 'Noch keine Geräte. Ein Admin kann Geräte anlegen.'}
        </p>
      )}

      {!loading && !error && !loadError && rooms.length > 0 && devices.length === 0 && hasFilter && (
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
                canEdit={isAdmin}
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
