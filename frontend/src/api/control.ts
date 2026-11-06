import { send } from './http'

// Der Server sendet den Tuya-Befehl und bestätigt anschließend den gelesenen Zustand.
export async function setDevicePower(deviceId: string, on: boolean): Promise<boolean> {
  const response = await send('/api/tuya/devices/' + encodeURIComponent(deviceId) + '/switch', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ on }),
  })
  const result = (await response.json()) as { deviceId: string; on: boolean; message: string }
  return result.on
}
