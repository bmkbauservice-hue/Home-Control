import { send } from './http'
import type { Device } from './devices'

// Der Server sendet den Befehl und antwortet erst, wenn das Gerät den Zustand bestätigt hat.
// Eine Google-Home-Verknüpfung hat Vorrang vor einem Tuya-Eintrag.
export async function setDevicePower(device: Pick<Device, 'id' | 'googleHomeDeviceId'>, on: boolean): Promise<boolean> {
  const route = device.googleHomeDeviceId ? '/api/google-home/devices/' : '/api/tuya/devices/'
  const response = await send(route + encodeURIComponent(device.id) + '/switch', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ on }),
  })
  const result = (await response.json()) as { deviceId: string; on: boolean; message: string }
  return result.on
}
