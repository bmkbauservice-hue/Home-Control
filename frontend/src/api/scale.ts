import { send } from './http'

export type ScaleField = {
  code: string
  label: string
  rawValue: unknown
  unit: string | null
  scale: number | null
  reportedAtUtc: string | null
}
export type ScaleReading = {
  binding: { tuyaId: string; name: string; weightCode: string | null } | null
  fields: ScaleField[]
  retrievedAtUtc: string | null
  note: string
}

export async function getScale(): Promise<ScaleReading> {
  const response = await send('/api/scale', { cache: 'no-store' })
  return await response.json() as ScaleReading
}

export async function selectScale(tuyaId: string | null, weightCode: string | null): Promise<void> {
  await send('/api/scale', { method: 'PUT', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tuyaId, weightCode }) })
}
