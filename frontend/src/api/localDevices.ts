import { send } from './http'

export type ShellyMeterReading = {
  id: number
  name: string
  state: 'online' | 'auth' | 'configuration' | 'unsupported' | 'unavailable'
  watt: number | null
  counterKwh: number | null
}

export async function getShellyMeters(signal?: AbortSignal): Promise<ShellyMeterReading[]> {
  const response = await send('/api/local-devices/shelly', { signal })
  return (await response.json()) as ShellyMeterReading[]
}
