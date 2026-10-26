import { BathIcon, BedIcon, DoorIcon, GuestIcon, KitchenIcon, SofaIcon } from './Icons'

type RoomIconProps = {
  name: string
  size?: number
}

// Wählt anhand des Raumnamens ein passendes Symbol, z. B. "Wohnzimmer (Demo)" -> Sofa.
// Die Reihenfolge ist wichtig: "Gästeschlafzimmer" soll das Gäste-Symbol bekommen,
// "Wohnküche" das Küchen-Symbol. Unbekannte Namen bekommen eine Tür.
export function RoomIcon({ name, size = 28 }: RoomIconProps) {
  const lower = name.toLocaleLowerCase('de')

  if (lower.includes('gäste') || lower.includes('gast')) return <GuestIcon size={size} />
  if (lower.includes('bad')) return <BathIcon size={size} />
  if (lower.includes('küche')) return <KitchenIcon size={size} />
  if (lower.includes('schlaf')) return <BedIcon size={size} />
  if (lower.includes('wohn')) return <SofaIcon size={size} />
  return <DoorIcon size={size} />
}
