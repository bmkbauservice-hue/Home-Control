import { send } from './http'
export type BridgeSensor = {
  sensorId: string
  name: string
  roomId: string | null
  temperatureCelsius: number | null
  humidityPercent: number | null
  updatedAtUtc: string
}
export async function getBridgeSensors(): Promise<BridgeSensor[]> {
  const response = await send('/api/climate/bridge/sensors')
  return (await response.json()) as BridgeSensor[]
}
export async function assignBridgeSensor(id: string, roomId: string | null): Promise<void> {
  await send('/api/climate/bridge/sensors/' + encodeURIComponent(id) + '/room', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ roomId })
  })
}
