import type { ReactNode } from 'react'
import { CameraIcon, DeviceIcon, HomeIcon, RoomsIcon } from './Icons'

// Die Seiten der App. Neue Seiten werden hier ergänzt.
export type Page = 'dashboard' | 'rooms' | 'devices' | 'blink'

const items: { page: Page; label: string; icon: ReactNode }[] = [
  { page: 'dashboard', label: 'Übersicht', icon: <HomeIcon /> },
  { page: 'rooms', label: 'Räume', icon: <RoomsIcon /> },
  { page: 'devices', label: 'Geräte', icon: <DeviceIcon /> },
  { page: 'blink', label: 'Blink', icon: <CameraIcon /> },
]

type NavigationProps = {
  currentPage: Page
  onNavigate: (page: Page) => void
}

// Auf dem Tablet links als Seitenleiste, auf dem Handy unten als Leiste.
// Das Umschalten passiert nur über CSS (siehe .nav in index.css).
export function Navigation({ currentPage, onNavigate }: NavigationProps) {
  return (
    <nav className="nav" aria-label="Hauptnavigation">
      <div className="nav-brand" aria-hidden="true">
        <HomeIcon size={28} />
        <span>Home Control</span>
      </div>
      {items.map((item) => (
        <button
          key={item.page}
          type="button"
          className={item.page === currentPage ? 'nav-item active' : 'nav-item'}
          aria-current={item.page === currentPage ? 'page' : undefined}
          onClick={() => onNavigate(item.page)}
        >
          {item.icon}
          <span>{item.label}</span>
        </button>
      ))}
    </nav>
  )
}
