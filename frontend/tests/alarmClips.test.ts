import assert from 'node:assert/strict'
import test from 'node:test'
import { hasMatchingEvent, isTimeMatch, matchingClips } from '../src/components/alarmClips.ts'

const door = '2026-11-08T22:54:55Z'

test('Clip kurz nach dem Türereignis passt zeitlich', () => {
  assert.equal(isTimeMatch(door, '2026-11-08T22:55:13Z'), true)
  assert.equal(isTimeMatch(door, '2026-11-08T22:57:55Z'), true)
})

test('Clip kurz vor dem Türereignis passt noch zeitlich', () => {
  assert.equal(isTimeMatch(door, '2026-11-08T22:54:25Z'), true)
})

test('Clips außerhalb des Zeitfensters passen nicht', () => {
  assert.equal(isTimeMatch(door, '2026-11-08T22:54:24Z'), false)
  assert.equal(isTimeMatch(door, '2026-11-08T22:57:56Z'), false)
  assert.equal(isTimeMatch(door, '2026-10-28T06:16:14Z'), false)
})

test('Ein Ereignis ohne passenden Clip ergibt eine leere Liste', () => {
  assert.deepEqual(matchingClips(door, [{ createdAtUtc: '2026-10-28T06:16:14Z' }]), [])
})

test('Clip erkennt ein passendes Ereignis', () => {
  assert.equal(hasMatchingEvent('2026-11-08T22:55:13Z', [{ startedAtUtc: door }]), true)
  assert.equal(hasMatchingEvent('2026-10-28T06:16:14Z', [{ startedAtUtc: door }]), false)
})
