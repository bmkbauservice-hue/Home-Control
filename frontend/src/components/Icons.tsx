import type { ReactNode } from 'react'

// Einfache Linien-Icons als SVG. Die Farbe kommt aus der CSS-Eigenschaft "color".

type IconProps = { size?: number }

function Icon({ size = 24, children }: IconProps & { children: ReactNode }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={2}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      {children}
    </svg>
  )
}

export function HomeIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M3 11 12 4l9 7" />
      <path d="M5 10v10h14V10" />
    </Icon>
  )
}

export function RoomsIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <rect x="3" y="3" width="8" height="8" rx="2" />
      <rect x="13" y="3" width="8" height="8" rx="2" />
      <rect x="3" y="13" width="8" height="8" rx="2" />
      <rect x="13" y="13" width="8" height="8" rx="2" />
    </Icon>
  )
}

export function CameraIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <rect x="3" y="6" width="13" height="12" rx="2" />
      <path d="m16 10 5-3v10l-5-3" />
    </Icon>
  )
}

export function DeviceIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M9 2v6M15 2v6" />
      <path d="M6 8h12v4a6 6 0 0 1-12 0z" />
      <path d="M12 18v4" />
    </Icon>
  )
}

export function HistoryIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <circle cx="12" cy="12" r="9" />
      <path d="M12 7v5l3 2" />
    </Icon>
  )
}

export function ChartIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M4 20h16" />
      <path d="M7 16v-5M12 16V6M17 16v-8" />
    </Icon>
  )
}

// ---- Raumsymbole (Auswahl über components/RoomIcon.tsx) ----

export function SofaIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M5 11V8a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2v3" />
      <path d="M3 18v-5a2 2 0 0 1 4 0v1h10v-1a2 2 0 0 1 4 0v5z" />
      <path d="M5 18v2M19 18v2" />
    </Icon>
  )
}

export function BedIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M3 19V6" />
      <path d="M3 15h18v4" />
      <path d="M21 15v-3a3 3 0 0 0-3-3h-7v6" />
      <circle cx="7" cy="12" r="2" />
    </Icon>
  )
}

export function BathIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M4 12h16v3a4 4 0 0 1-4 4H8a4 4 0 0 1-4-4z" />
      <path d="M6 12V6a2 2 0 0 1 4 0" />
      <path d="M7 19l-1 2M17 19l1 2" />
    </Icon>
  )
}

export function KitchenIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M5 11h14v5a4 4 0 0 1-4 4H9a4 4 0 0 1-4-4z" />
      <path d="M3 11h18" />
      <path d="M9 4v3M12 3v4M15 4v3" />
    </Icon>
  )
}

export function GuestIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <rect x="5" y="7" width="14" height="13" rx="2" />
      <path d="M9 7V4h6v3" />
      <path d="M9 11v5M15 11v5" />
    </Icon>
  )
}

export function DoorIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M6 21V4a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v17" />
      <path d="M3 21h18" />
      <path d="M14 12h.01" />
    </Icon>
  )
}

// ---- Gerätetyp-Symbole (Auswahl über components/DeviceTypeIcon.tsx) ----

export function LampIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M9 18h6M10 21h4" />
      <path d="M12 3a6 6 0 0 0-3.5 10.9c.6.5 1 1.2 1 2.1h5c0-.9.4-1.6 1-2.1A6 6 0 0 0 12 3z" />
    </Icon>
  )
}

export function ThermometerIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M14 14.8V5a2 2 0 0 0-4 0v9.8a4 4 0 1 0 4 0z" />
      <path d="M12 9v7" />
    </Icon>
  )
}

export function SwitchIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <rect x="6" y="3" width="12" height="18" rx="2" />
      <rect x="10" y="7" width="4" height="10" rx="1" />
    </Icon>
  )
}

export function ChevronRightIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="m9 6 6 6-6 6" />
    </Icon>
  )
}

export function PlusIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M12 5v14M5 12h14" />
    </Icon>
  )
}

export function EditIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M4 20h4L19 9l-4-4L4 16z" />
    </Icon>
  )
}

export function TrashIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M4 7h16M10 11v6M14 11v6" />
      <path d="M6 7l1 13h10l1-13M9 7V4h6v3" />
    </Icon>
  )
}

export function LogoutIcon(props: IconProps) {
  return (
    <Icon {...props}>
      <path d="M9 4H5v16h4" />
      <path d="M16 8l4 4-4 4" />
      <path d="M20 12H9" />
    </Icon>
  )
}
