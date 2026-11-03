import type { SystemInfo } from '../../api/system'

type SystemInfoCardProps = {
  info: SystemInfo | null
  error: string | null
}

const timeFormat = new Intl.DateTimeFormat('de-DE', { dateStyle: 'short', timeStyle: 'short' })

// 1234567 Bytes → "1,2 MB"
function sizeText(bytes: number | null): string {
  if (bytes === null) return 'unbekannt'
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / 1024 / 1024).toLocaleString('de-DE', { maximumFractionDigits: 1 })} MB`
}

// Info: Was läuft hier gerade? Version mit Commit, Umgebung, Datenbankgröße, Bestand.
export function SystemInfoCard({ info, error }: SystemInfoCardProps) {
  return (
    <section className="tile settings-card" aria-labelledby="settings-info">
      <h2 id="settings-info" className="settings-title">
        Info
      </h2>
      {error !== null && (
        <p className="alert" role="alert">
          {error}
        </p>
      )}
      {info === null ? (
        error === null && <p className="tile-text">Wird geladen …</p>
      ) : (
        <dl className="settings-facts">
          <dt>Version</dt>
          <dd>
            {info.version}
            {info.commit !== null && <span className="settings-muted"> · Commit {info.commit}</span>}
          </dd>
          <dt>Umgebung</dt>
          <dd>
            {info.environment}
            {info.demoMode && <span className="settings-badge settings-badge-warning">Demo</span>}
          </dd>
          <dt>Läuft seit</dt>
          <dd>{timeFormat.format(new Date(info.startedAtUtc))} Uhr</dd>
          <dt>Datenbank</dt>
          <dd>
            {info.databaseFile} · {sizeText(info.databaseBytes)}
          </dd>
          <dt>Bestand</dt>
          <dd>
            {info.users} Benutzer · {info.rooms} Räume · {info.devices} Geräte · {info.events.toLocaleString('de-DE')}{' '}
            Ereignisse
          </dd>
          <dt>Laufzeit</dt>
          <dd>{info.dotNetVersion}</dd>
        </dl>
      )}
    </section>
  )
}
