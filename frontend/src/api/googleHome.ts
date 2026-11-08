import { send } from './http'

// Ein Gerät, wie Muju Home es aus Google Home meldet.
export type GoogleHomeDevice = {
  id: string
  name: string
  online: boolean
  // null = kein lesbarer Ein/Aus-Zustand
  on: boolean | null
  // Türsensor: true = offen, null = kein Türsensor
  open?: boolean | null
  // Reiner Messpunkt, der nie geschaltet werden darf
  protected: boolean
}

export type GoogleHomeDevices = {
  connected: boolean
  devices: GoogleHomeDevice[]
}

export async function getGoogleHomeDevices(): Promise<GoogleHomeDevices> {
  const response = await send('/api/google-home/devices')
  return (await response.json()) as GoogleHomeDevices
}
