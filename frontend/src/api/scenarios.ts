import { send } from './http'

// Regeln: "Wenn diese Tür aufgeht, schalte dieses Blink-System scharf."

export type ScenarioRun = {
  startedAtUtc: string
  finishedAtUtc: string | null
  // "tuer" = echter Wechsel von Zu auf Auf, "test" = von Hand ausgelöst
  trigger: string
  outcome: 'laeuft' | 'bestaetigt' | 'nicht-bestaetigt' | 'fehler'
  message: string
}

export type Scenario = {
  id: string
  name: string
  enabled: boolean
  triggerDeviceId: string
  triggerDeviceName: string
  blinkSystemId: string
  blinkSystemName: string
  runs: ScenarioRun[]
}

export type ScenarioInput = { name: string; triggerDeviceId: string; blinkSystemId: string }

export type ScenarioOptions = {
  // hasFreshSignal: Der Sensor ist mit Google Home verknüpft und meldet Wechsel sofort.
  doors: { id: string; name: string; roomName: string; hasFreshSignal: boolean }[]
  systems: { id: string; name: string; armed: boolean | null }[]
  systemsError: string | null
}

export async function getScenarios(): Promise<Scenario[]> {
  return (await (await send('/api/scenarios')).json()) as Scenario[]
}

export async function getScenarioOptions(): Promise<ScenarioOptions> {
  return (await (await send('/api/scenarios/options')).json()) as ScenarioOptions
}

function jsonOptions(method: string, body: unknown) {
  return { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
}

export async function saveScenario(id: string | null, input: ScenarioInput): Promise<void> {
  await send(id === null ? '/api/scenarios' : `/api/scenarios/${id}`, jsonOptions(id === null ? 'POST' : 'PUT', input))
}

export async function setScenarioEnabled(id: string, enabled: boolean): Promise<void> {
  await send(`/api/scenarios/${id}/enabled`, jsonOptions('PUT', { enabled }))
}

export async function deleteScenario(id: string): Promise<void> {
  await send(`/api/scenarios/${id}`, { method: 'DELETE' })
}

export async function runScenario(id: string): Promise<void> {
  await send(`/api/scenarios/${id}/run`, { method: 'POST' })
}
