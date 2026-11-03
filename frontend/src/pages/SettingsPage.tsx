import { useEffect, useState } from 'react'
import { getSystemInfo, type SystemInfo } from '../api/system'
import { ConnectionsCard } from '../components/settings/ConnectionsCard'
import { PasswordForm } from '../components/settings/PasswordForm'
import { SyncTable } from '../components/settings/SyncTable'
import { SystemInfoCard } from '../components/settings/SystemInfoCard'
import { UserAdmin } from '../components/settings/UserAdmin'

// Einstellungen (nur Admin, siehe App.tsx und Navigation): Benutzer, eigenes Passwort, Verbindungen,
// Abgleich-Takt je Gerät und Info. Jeder Bereich lädt und speichert für sich, ein Fehler stört die anderen nicht.
export function SettingsPage() {
  const [info, setInfo] = useState<SystemInfo | null>(null)
  const [infoError, setInfoError] = useState<string | null>(null)

  // Die Info braucht auch die Abgleich-Tabelle (Standard-Takt), deshalb wird sie hier einmal geladen.
  useEffect(() => {
    getSystemInfo()
      .then(setInfo)
      .catch((err: Error) => setInfoError(err.message))
  }, [])

  return (
    <section>
      <header className="page-header">
        <h1>Einstellungen</h1>
        <p className="page-subtitle">Benutzer, Verbindungen, Abgleich und Info. Nur für Admins.</p>
      </header>

      <div className="settings-grid">
        <UserAdmin />
        <ConnectionsCard />
        <PasswordForm />
        <SyncTable defaultSeconds={info?.tuyaDefaultSyncSeconds ?? null} />
        <SystemInfoCard info={info} error={infoError} />
      </div>
    </section>
  )
}
