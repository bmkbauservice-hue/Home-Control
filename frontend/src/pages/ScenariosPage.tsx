import { useEffect, useState, type FormEvent } from 'react'
import {
  deleteScenario,
  getScenarioOptions,
  getScenarios,
  runScenario,
  saveScenario,
  setScenarioEnabled,
  type Scenario,
  type ScenarioOptions,
  type ScenarioRun,
} from '../api/scenarios'
import { ErrorMessage, LoadingMessage } from '../components/StateMessages'

const emptyForm = { name: '', triggerDeviceId: '', blinkSystemId: '' }

const outcomeText: Record<ScenarioRun['outcome'], string> = {
  laeuft: 'Läuft',
  bestaetigt: 'Bestätigt',
  'nicht-bestaetigt': 'Nicht bestätigt',
  fehler: 'Fehler',
}

function formatTime(value: string): string {
  return new Date(value).toLocaleString('de-DE', { day: '2-digit', month: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

// Regeln anlegen, bearbeiten und ein- oder ausschalten. Neue Regeln starten ausgeschaltet.
export function ScenariosPage() {
  const [scenarios, setScenarios] = useState<Scenario[] | null>(null)
  const [options, setOptions] = useState<ScenarioOptions | null>(null)
  const [form, setForm] = useState(emptyForm)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  function reload() {
    getScenarios().then(setScenarios).catch((err: Error) => setError(err.message))
  }

  useEffect(() => {
    getScenarios().then(setScenarios).catch((err: Error) => setError(err.message))
    getScenarioOptions().then(setOptions).catch((err: Error) => setError(err.message))
    // Läufe enden im Hintergrund, deshalb den Verlauf regelmäßig neu lesen.
    const timer = window.setInterval(() => {
      getScenarios().then(setScenarios).catch(() => undefined)
    }, 5000)
    return () => window.clearInterval(timer)
  }, [])

  async function act(action: () => Promise<void>) {
    setBusy(true)
    setError(null)
    try {
      await action()
      reload()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    void act(async () => {
      await saveScenario(editingId, form)
      setForm(emptyForm)
      setEditingId(null)
    })
  }

  function startEdit(scenario: Scenario) {
    setEditingId(scenario.id)
    setForm({ name: scenario.name, triggerDeviceId: scenario.triggerDeviceId, blinkSystemId: scenario.blinkSystemId })
  }

  function handleTest(scenario: Scenario) {
    // Der Test wirkt wirklich: Danach ist das System scharf und muss in der Blink-App wieder entschärft werden.
    if (window.confirm(`„${scenario.blinkSystemName}“ jetzt wirklich scharfschalten? Entschärfen geht danach in der Blink-App.`)) {
      void act(() => runScenario(scenario.id))
    }
  }

  if (scenarios === null && error === null) return <LoadingMessage text="Szenarien werden geladen …" />

  const doorWithoutSignal = options?.doors.find((door) => door.id === form.triggerDeviceId && !door.hasFreshSignal)

  return (
    <section className="scenarios-page">
      <h1>Szenarien</h1>
      {error && <ErrorMessage message={error} onRetry={reload} />}

      <form className="tile scenario-form" onSubmit={handleSubmit}>
        <h2>{editingId === null ? 'Neue Regel' : 'Regel bearbeiten'}</h2>
        <div className="field">
          <label className="field-label" htmlFor="scenario-name">Name</label>
          <input id="scenario-name" className="input" maxLength={100} value={form.name}
            onChange={(event) => setForm({ ...form, name: event.target.value })} />
        </div>
        <div className="field">
          <label className="field-label" htmlFor="scenario-door">Wenn diese Tür aufgeht</label>
          <select id="scenario-door" className="input" value={form.triggerDeviceId}
            onChange={(event) => setForm({ ...form, triggerDeviceId: event.target.value })}>
            <option value="">Bitte wählen</option>
            {options?.doors.map((door) => (
              <option key={door.id} value={door.id}>{door.name} · {door.roomName}</option>
            ))}
          </select>
          {doorWithoutSignal && (
            <p className="field-note">Dieser Sensor braucht zuerst unter Geräte eine Google-Home-Verknüpfung, sonst gibt es kein frisches Türsignal.</p>
          )}
        </div>
        <div className="field">
          <label className="field-label" htmlFor="scenario-system">Dann dieses Blink-System scharfschalten</label>
          <select id="scenario-system" className="input" value={form.blinkSystemId}
            onChange={(event) => setForm({ ...form, blinkSystemId: event.target.value })}>
            <option value="">Bitte wählen</option>
            {options?.systems.map((system) => (
              <option key={system.id} value={system.id}>
                {system.name}{system.armed === true ? ' · gerade scharf' : system.armed === false ? ' · gerade unscharf' : ''}
              </option>
            ))}
          </select>
          {options?.systemsError && <p className="field-note">{options.systemsError}</p>}
        </div>
        <div className="tile-actions">
          <button type="submit" className="button button-primary"
            disabled={busy || form.name.trim() === '' || form.triggerDeviceId === '' || form.blinkSystemId === ''}>
            {editingId === null ? 'Regel anlegen (startet aus)' : 'Speichern'}
          </button>
          {editingId !== null && (
            <button type="button" className="button" onClick={() => { setEditingId(null); setForm(emptyForm) }}>Abbrechen</button>
          )}
        </div>
      </form>

      {scenarios?.length === 0 && <p className="empty">Noch keine Regeln angelegt.</p>}

      {scenarios?.map((scenario) => (
        <article key={scenario.id} className="tile scenario-card">
          <div className="scenario-head">
            <div>
              <h2>{scenario.name}</h2>
              <p className="energy-meta">Wenn {scenario.triggerDeviceName} von Zu auf Auf wechselt, wird {scenario.blinkSystemName} scharfgeschaltet.</p>
            </div>
            <button type="button" role="switch" aria-checked={scenario.enabled} aria-label={`Regel ${scenario.name}`}
              className={'device-switch' + (scenario.enabled ? ' is-on' : '')} disabled={busy}
              onClick={() => void act(() => setScenarioEnabled(scenario.id, !scenario.enabled))}>
              <span className="device-switch-thumb" />
              <span className="device-switch-label">{scenario.enabled ? 'Ein' : 'Aus'}</span>
            </button>
          </div>
          <div className="tile-actions">
            <button type="button" className="button" disabled={busy} onClick={() => startEdit(scenario)}>Bearbeiten</button>
            <button type="button" className="button" disabled={busy} onClick={() => handleTest(scenario)}>Einmal testen</button>
            <button type="button" className="button" disabled={busy} onClick={() => void act(() => deleteScenario(scenario.id))}>Löschen</button>
          </div>
          <h3 className="energy-label">Verlauf</h3>
          {scenario.runs.length === 0 ? <p className="energy-meta">Diese Regel lief noch nie.</p> : (
            <ul className="scenario-runs">
              {scenario.runs.map((run) => (
                <li key={run.startedAtUtc}>
                  <span>{formatTime(run.startedAtUtc)} · {run.trigger === 'tuer' ? 'Tür ging auf' : 'Test'}</span>
                  <strong className={`scenario-outcome scenario-outcome-${run.outcome}`}>{outcomeText[run.outcome]}</strong>
                  <small>{run.message}</small>
                </li>
              ))}
            </ul>
          )}
        </article>
      ))}
    </section>
  )
}
