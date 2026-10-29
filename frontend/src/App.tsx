import { useState } from 'react'
import { Navigation, type Page } from './components/Navigation'
import { BlinkPage } from './pages/BlinkPage'
import { DashboardPage } from './pages/DashboardPage'
import { DevicesPage } from './pages/DevicesPage'
import { ReportsPage } from './pages/ReportsPage'
import { RoomsPage } from './pages/RoomsPage'
import { LoginPage } from './pages/LoginPage'
import { useAuth } from './auth/authContext'

export default function App() {
  const { status } = useAuth()

  // Ohne Anmeldung sieht man nur die Anmeldeseite. Solange noch geprüft wird, ob eine Sitzung
  // besteht, bleibt der Bildschirm leer, damit die Anmeldeseite nicht kurz aufblitzt.
  if (status === 'loading') {
    return null
  }
  if (status === 'anonymous') {
    return <LoginPage />
  }
  return <AppShell />
}

// Die eigentliche App für angemeldete Benutzer.
function AppShell() {
  // Welche Seite gerade angezeigt wird. Ein Router ist für fünf Seiten noch nicht nötig.
  const [page, setPage] = useState<Page>('dashboard')
  // Raum, mit dem die Geräteseite gefiltert starten soll ('' = alle Räume).
  const [devicesRoomId, setDevicesRoomId] = useState('')

  // Normale Navigation: Geräteseite immer ohne Raumfilter öffnen.
  function navigate(next: Page) {
    setDevicesRoomId('')
    setPage(next)
  }

  // Von einer Raumkarte aus: Geräteseite mit diesem Raum als Filter öffnen.
  function openDevicesInRoom(roomId: string) {
    setDevicesRoomId(roomId)
    setPage('devices')
  }

  return (
    <div className="app">
      <Navigation currentPage={page} onNavigate={navigate} />
      <main className="main">
        {page === 'dashboard' && <DashboardPage onNavigate={navigate} onOpenRoom={openDevicesInRoom} />}
        {page === 'rooms' && <RoomsPage />}
        {page === 'devices' && <DevicesPage onNavigate={navigate} initialRoomId={devicesRoomId} />}
        {page === 'reports' && <ReportsPage />}
        {page === 'blink' && <BlinkPage />}
      </main>
    </div>
  )
}
