import { useState } from 'react'
import { setDevicePower } from '../api/control'
import type { Device } from '../api/devices'
import { useAuth } from '../auth/authContext'
import { useDemoMode } from '../hooks/useDemoMode'

type DeviceSwitchProps = {
  device: Device
  onlineState: string
  onSwitched: () => void
  compact?: boolean
}

export function DeviceSwitch({ device, onlineState, onSwitched, compact = false }: DeviceSwitchProps) {
  const { isAdmin } = useAuth()
  const demo = useDemoMode()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Sensoren, Kameras und unverknüpfte Geräte dürfen keine Schaltknöpfe bekommen.
  const switchable = ['Light', 'Socket', 'Switch'].includes(device.type)
    && Boolean(device.tuyaDeviceId)
    && ['EIN', 'AUS'].includes(device.lastValue ?? '')
  if (!switchable || !isAdmin) return null

  const isOn = device.lastValue === 'EIN'
  const blocked = demo || busy || onlineState === 'offline'

  async function switchPower() {
    if (blocked) return
    setBusy(true)
    setError(null)
    try {
      await setDevicePower(device.id, !isOn)
      onSwitched()
    } catch (err) {
      setError((err as Error).message)
      // Auch bei unbestätigtem Schalten den vom Backend bekannten Zustand neu laden.
      onSwitched()
    } finally {
      setBusy(false)
    }
  }

  return (
    <span className={'device-switch-wrap' + (compact ? ' device-switch-compact' : '')}>
      <button
        type="button"
        className={'device-switch' + (isOn ? ' is-on' : '')}
        role="switch"
        aria-checked={isOn}
        aria-label={device.name + (isOn ? ' ausschalten' : ' einschalten')}
        title={demo ? 'Im Demo-Modus nicht schaltbar' : onlineState === 'offline' ? 'Gerät ist offline' : device.name}
        disabled={blocked}
        onClick={() => void switchPower()}
      >
        <span className="device-switch-thumb" />
        {!compact && <span className="device-switch-label">{busy ? 'Schalte …' : isOn ? 'Ein' : 'Aus'}</span>}
      </button>
      {error && <span className="device-switch-error" role="alert">{error}</span>}
    </span>
  )
}
