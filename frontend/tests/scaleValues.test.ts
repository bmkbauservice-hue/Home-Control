import assert from 'node:assert/strict'
import test from 'node:test'
import { scaleValue, rawScaleValue, scaleTime } from '../src/components/scaleValues.ts'

test('Skalierung ausschließlich aus der Felddefinition', () => {
  assert.equal(scaleValue({ code: 'weight', label: 'Gewicht', rawValue: 7234, unit: 'kg', scale: 2, reportedAtUtc: null }), '72,34 kg')
  assert.equal(scaleValue({ code: 'unknown', label: 'Unbekannt', rawValue: 7234, unit: null, scale: null, reportedAtUtc: null }), '7234 (Rohwert)')
})

test('Fehlende Werte und Zeitpunkte werden nicht ersetzt', () => {
  assert.equal(rawScaleValue(null), 'Kein Wert')
  assert.equal(scaleTime(null), 'Zeitpunkt unbekannt')
  assert.equal(scaleTime('keine Zeit'), 'Zeitpunkt unbekannt')
})

test('Unbekannte Zusatzwerte bleiben unverändert sichtbar', () => {
  assert.equal(rawScaleValue({ value: 42 }), '{"value":42}')
  assert.equal(scaleValue({ code: 'custom', label: 'custom', rawValue: true, unit: null, scale: null, reportedAtUtc: null }), 'true')
})
