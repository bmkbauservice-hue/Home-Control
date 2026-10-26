import { useEffect, useState } from 'react'
import { getRooms } from '../api/rooms'
import { BlinkStatus } from '../components/BlinkStatus'
import { CameraIcon, RoomsIcon } from '../components/Icons'
import type { Page } from '../components/Navigation'

type DashboardPageProps = {
  onNavigate: (page: Page) => void
}

export function DashboardPage({ onNavigate }: DashboardPageProps) {
  // null bedeutet: noch nicht geladen
  const [roomCount, setRoomCount] = useState<number | null>(null)
  const [error, setError] = useState(false)

  useEffect(() => {
    getRooms()
      .then((rooms) => setRoomCount(rooms.length))
      .catch(() => setError(true))
  }, [])

  let roomText = 'Wird geladen …'
  if (error) {
    roomText = 'API nicht erreichbar'
  } else if (roomCount !== null) {
    roomText = roomCount === 1 ? '1 Raum' : `${roomCount} Räume`
  }

  return (
    <section>
      <header className="page-header">
        <h1>Night Control</h1>
        <p className="page-subtitle">Dein Zuhause auf einen Blick</p>
      </header>

      <div className="tile-grid">
        <button type="button" className="tile tile-link" onClick={() => onNavigate('rooms')}>
          <span className="tile-icon">
            <RoomsIcon size={32} />
          </span>
          <span className="tile-title">Räume</span>
          <span className="tile-text">{roomText}</span>
        </button>

        <button type="button" className="tile tile-link" onClick={() => onNavigate('blink')}>
          <span className="tile-icon">
            <CameraIcon size={32} />
          </span>
          <span className="tile-title">Blink</span>
          <BlinkStatus />
        </button>
      </div>
    </section>
  )
}
