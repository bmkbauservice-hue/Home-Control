import type { ScaleField } from '../api/scale'

export function rawScaleValue(value: unknown): string {
  if (value === null || value === undefined) return 'Kein Wert'
  return typeof value === 'string' ? value : JSON.stringify(value)
}

export function scaleValue(field: ScaleField): string {
  if (typeof field.rawValue === 'number' && field.scale !== null) {
    const value = field.rawValue / 10 ** field.scale
    return value.toLocaleString('de-DE', { maximumFractionDigits: Math.min(field.scale, 9) }) + (field.unit ? ' ' + field.unit : '')
  }
  return rawScaleValue(field.rawValue) + (typeof field.rawValue === 'number' ? ' (Rohwert)' : '')
}

export function scaleTime(value: string | null): string {
  if (!value) return 'Zeitpunkt unbekannt'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? 'Zeitpunkt unbekannt' : date.toLocaleString('de-DE')
}
