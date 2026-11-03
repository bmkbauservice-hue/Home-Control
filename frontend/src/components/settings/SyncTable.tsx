import { useEffect, useState } from 'react'
import { deviceToInput, getDevices, tuyaSyncOptions, updateDevice, type Device } from '../../api/devices'
import { useStatus } from '../../status/statusContext'
import { measuredAtText } from '../measurements'
import { StatusLed } from '../StatusLed'

type SyncTableProps = {
  // Standard-Takt laut Server (Tuya:AutoSyncSeconds), null solange unbekannt. 0 = Hintergrund-Abgleich aus.
  defaultSeconds: number | null
}

function minutesText(seconds: number): string {
  return seconds % 60 === 0 ? `${seconds / 60} min` : `${seconds} s`
}

// Abgleich: alle mit Tuya verknüpften Geräte in einer Tabelle, Takt direkt änderbar.
// Speichert über PUT /api/devices/{id} (wie "Bearbeiten" auf der Geräteseite), nur der Takt ändert sich.
export function SyncTable({ defaultSeconds }: SyncTableProps) {
  const { stateOf } = useStatus()
  const [devices, setDevices] = useState<Device[] | null>(null)
  const [savingId, setSavingId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    getDevices()
      .then((loaded) => setDevices(loaded.filter((d) => d.tuyaDeviceId !== null)))
      .catch((err: Error) => setError(err.message))
  }, [])

  async function changeInterval(device: Device, seconds: number | null) {
    setSavingId(device.id)
    setError(null)
    try {
      const updated = await updateDevice(device.id, { ...deviceToInput(device), tuyaSyncSeconds: seconds })
      setDevices((current) => current?.map((d) => (d.id === updated.id ? updated : d)) ?? null)
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setSavingId(null)
    }
  }

  const standard =
    defaultSeconds === null ? 'Standard' : defaultSeconds <= 0 ? 'Standard (Abgleich aus)' : `Standard (${minutesText(defaultSeconds)})`

  return (
    <section className="tile settings-card settings-wide" aria-labelledby="settings-sync">
      <h2 id="settings-sync" className="settings-title">
        Abgleich mit Tuya
      </h2>
      <p className="settings-muted">
        Wie oft der Hintergrund-Abgleich jedes Gerät bei Tuya abfragt. Kürzer heißt aktueller, kostet aber Kontingent
        (Rechnung in docs/TUYA.md).
      </p>
      {error !== null && (
        <p className="alert" role="alert">
          {error}
        </p>
      )}
      {devices === null ? (
        error === null && <p className="tile-text">Wird geladen …</p>
      ) : devices.length === 0 ? (
        <p className="empty">Noch kein Gerät mit Tuya verknüpft.</p>
      ) : (
        <div className="table-scroll" tabIndex={0} aria-label="Abgleich-Takt je Gerät">
          <table className="settings-table">
            <thead>
              <tr>
                <th scope="col">Gerät</th>
                <th scope="col" className="hide-small">
                  Raum
                </th>
                <th scope="col">Takt</th>
                <th scope="col">Letzte Messung</th>
              </tr>
            </thead>
            <tbody>
              {devices.map((d) => (
                <tr key={d.id}>
                  <td>
                    <StatusLed state={stateOf(d.id)} label={d.name} />
                  </td>
                  <td className="settings-muted hide-small">{d.roomName}</td>
                  <td>
                    <label className="visually-hidden" htmlFor={`sync-${d.id}`}>
                      Abgleich-Takt für {d.name}
                    </label>
                    <select
                      id={`sync-${d.id}`}
                      className="input input-compact"
                      value={d.tuyaSyncSeconds ?? ''}
                      disabled={savingId === d.id}
                      onChange={(event) =>
                        void changeInterval(d, event.target.value === '' ? null : Number(event.target.value))
                      }
                    >
                      {tuyaSyncOptions.map((option) => (
                        <option key={option.seconds ?? 'standard'} value={option.seconds ?? ''}>
                          {option.seconds === null ? standard : option.label}
                        </option>
                      ))}
                    </select>
                  </td>
                  <td className="settings-muted">{measuredAtText(d.measuredAtUtc) ?? '–'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  )
}
