import { useEffect, useState } from 'react'
import { getEventActionLabel } from '../api/deviceEvents'
import { formatIsoDate, getDailyReport, getTodayUtc, type DailyReport } from '../api/reports'
import { DeviceIcon, HistoryIcon } from '../components/Icons'

export function ReportsPage() {
  const [date, setDate] = useState(getTodayUtc())
  const [report, setReport] = useState<DailyReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Neues Datum gewählt: Ladeanzeige an, alte Fehlermeldung weg. Laden übernimmt der Effect unten.
  function changeDate(value: string) {
    setDate(value)
    setLoading(true)
    setError(null)
  }

  // Bei jedem Datumswechsel den Bericht neu laden.
  useEffect(() => {
    // Ein leeres Feld (z. B. nach "Löschen" in der Datumsauswahl) nicht abfragen.
    if (date === '') {
      return
    }

    // Wechselt das Datum schnell hintereinander, soll eine ältere, langsamere
    // Antwort den neueren Bericht nicht überschreiben.
    let ignore = false

    getDailyReport(date)
      .then((loaded) => {
        if (!ignore) setReport(loaded)
      })
      .catch((err: Error) => {
        if (!ignore) {
          setReport(null)
          setError(err.message)
        }
      })
      .finally(() => {
        if (!ignore) setLoading(false)
      })

    return () => {
      ignore = true
    }
  }, [date])

  return (
    <section className="reports">
      <header className="page-header">
        <h1>Reports</h1>
        <p className="page-subtitle">Tagesauswertung der Geräteereignisse</p>
      </header>

      <div className="tile reports-controls">
        <div className="field">
          <label className="field-label" htmlFor="report-date">
            Tag (UTC)
          </label>
          <input
            id="report-date"
            type="date"
            className="input"
            value={date}
            onChange={(event) => changeDate(event.target.value)}
          />
        </div>

        <div className="utc-note" role="note">
          <strong>Auswertung nach UTC</strong>
          <span>
            Ein Tag zählt hier von 00:00 bis 24:00 Uhr UTC. In Deutschland ist das je nach Winter- oder
            Sommerzeit von 01:00 bzw. 02:00 Uhr bis zur gleichen Uhrzeit am Folgetag.
          </span>
        </div>
      </div>

      {date === '' && <p className="empty">Bitte einen Tag auswählen.</p>}

      {date !== '' && loading && <p className="empty">Bericht wird geladen …</p>}

      {date !== '' && !loading && error && (
        <p className="alert" role="alert">
          {error}
        </p>
      )}

      {date !== '' && !loading && report && (
        <>
          <div className="summary-grid">
            <div className="tile summary-card">
              <span className="tile-icon">
                <DeviceIcon size={28} />
              </span>
              <span className="summary-label">Aktueller Gerätebestand</span>
              <span className="summary-value">{report.currentDeviceCount}</span>
              <span className="tile-text">Stand jetzt – nicht der Bestand am gewählten Tag</span>
            </div>

            <div className="tile summary-card">
              <span className="tile-icon">
                <HistoryIcon size={28} />
              </span>
              <span className="summary-label">Ereignisse am {formatIsoDate(report.date)}</span>
              <span className="summary-value">{report.eventCount}</span>
              <span className="tile-text">00:00–24:00 Uhr UTC</span>
            </div>
          </div>

          {report.eventCount === 0 ? (
            <p className="empty">
              Am {formatIsoDate(report.date)} (UTC) wurden keine Ereignisse erfasst.
            </p>
          ) : (
            <div className="reports-columns">
              <section className="tile" aria-labelledby="actions-heading">
                <h2 id="actions-heading" className="report-heading">
                  Aktionen
                </h2>
                <ul className="report-list">
                  {report.actions.map((item) => (
                    <li key={item.action} className="report-row">
                      <span>{getEventActionLabel(item.action)}</span>
                      <span className="report-count">{item.count}</span>
                    </li>
                  ))}
                </ul>
              </section>

              <section className="tile" aria-labelledby="top-devices-heading">
                <h2 id="top-devices-heading" className="report-heading">
                  Geräte mit den meisten Ereignissen
                </h2>
                <ol className="report-list">
                  {report.topDevices.map((device, index) => (
                    <li key={device.deviceId} className="report-row">
                      <span className="report-rank">{index + 1}.</span>
                      <span className="report-name">{device.deviceName}</span>
                      <span className="report-count">{device.eventCount}</span>
                    </li>
                  ))}
                </ol>
                <p className="tile-text report-footnote">Höchstens fünf Geräte, nur Geräte mit Ereignissen.</p>
              </section>
            </div>
          )}
        </>
      )}
    </section>
  )
}
