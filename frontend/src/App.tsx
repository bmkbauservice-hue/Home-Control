import { useState } from 'react'
import { Navigation, type Page } from './components/Navigation'
import { BlinkPage } from './pages/BlinkPage'
import { DashboardPage } from './pages/DashboardPage'
import { DevicesPage } from './pages/DevicesPage'
import { ReportsPage } from './pages/ReportsPage'
import { RoomsPage } from './pages/RoomsPage'

export default function App() {
  // Welche Seite gerade angezeigt wird. Ein Router ist für fünf Seiten noch nicht nötig.
  const [page, setPage] = useState<Page>('dashboard')

  return (
    <div className="app">
      <Navigation currentPage={page} onNavigate={setPage} />
      <main className="main">
        {page === 'dashboard' && <DashboardPage onNavigate={setPage} />}
        {page === 'rooms' && <RoomsPage />}
        {page === 'devices' && <DevicesPage onNavigate={setPage} />}
        {page === 'reports' && <ReportsPage />}
        {page === 'blink' && <BlinkPage />}
      </main>
    </div>
  )
}
