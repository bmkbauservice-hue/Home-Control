import { DeviceIcon, DoorIcon, LampIcon, SwitchIcon, ThermometerIcon } from './Icons'

type DeviceTypeIconProps = {
  type: string
  size?: number
}

// Passendes Symbol zum Gerätetyp. Unbekannte (ältere) Typen bekommen das Stecker-Symbol.
export function DeviceTypeIcon({ type, size = 20 }: DeviceTypeIconProps) {
  switch (type) {
    case 'Light':
      return <LampIcon size={size} />
    case 'TemperatureSensor':
      return <ThermometerIcon size={size} />
    case 'DoorSensor':
      return <DoorIcon size={size} />
    case 'Switch':
      return <SwitchIcon size={size} />
    default:
      return <DeviceIcon size={size} />
  }
}
