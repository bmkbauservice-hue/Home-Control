import { send } from './http'

export type RoomClimate = {
  roomId: string
  temperatureCelsius: number | null
  humidityPercent: number | null
  updatedAtUtc: string
  source: string
  stale: boolean
}

export type BridgeSensor = {
  sensorId: string
  name: string
  roomId: string | null
  temperatureCelsius: number | null
  humidityPercent: number | null
  updatedAtUtc: string
  stale: boolean
}

export type ClimateStatus = {
  bridgeConfigured: boolean
  sensors: number
  assigned: number
  lastReceivedAtUtc: string | null
  hasFreshReadings: boolean
}

export async function getClimate(): Promise<RoomClimate[]> {
  const response = await send('/api/climate')
  return (await response.json()) as RoomClimate[]
}

export async function getBridgeSensors(): Promise<BridgeSensor[]> {
  const response = await send('/api/climate/bridge/sensors')
  return (await response.json()) as BridgeSensor[]
}

export async function getClimateStatus(): Promise<ClimateStatus> {
  const response = await send('/api/climate/bridge/status')
  return (await response.json()) as ClimateStatus
}

export async function assignBridgeSensor(id: string, roomId: string | null): Promise<void> {
  await send('/api/climate/bridge/sensors/' + encodeURIComponent(id) + '/room', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ roomId }),
  })
}
