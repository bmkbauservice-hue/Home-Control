import { useState, type FormEvent } from 'react'
import { HomeIcon } from '../components/Icons'
import { useAuth } from '../auth/authContext'

// Anmeldeseite. Beim allerersten Start gibt es noch keinen Benutzer: Dann legt man hier das
// erste Konto an, das automatisch Admin wird. Weitere Benutzer legt später nur ein Admin an.
export function LoginPage() {
  const { signIn, setUpFirstAccount } = useAuth()
  const [firstAccount, setFirstAccount] = useState(false)
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await (firstAccount ? setUpFirstAccount(email.trim(), password) : signIn(email.trim(), password))
      // Bei Erfolg wechselt AuthProvider auf "angemeldet" und diese Seite verschwindet.
    } catch (err) {
      setError((err as Error).message)
      setBusy(false)
    }
  }

  return (
    <main className="login">
      <form className="tile login-card" onSubmit={handleSubmit}>
        <div className="login-brand">
          <HomeIcon size={36} />
          <h1>Home Control</h1>
        </div>
        <p className="tile-text">
          {firstAccount
            ? 'Erstes Konto anlegen. Es wird automatisch Admin.'
            : 'Bitte melde dich an.'}
        </p>

        <div className="field">
          <label className="field-label" htmlFor="login-email">
            E-Mail
          </label>
          <input
            id="login-email"
            type="email"
            className="input"
            autoComplete="username"
            autoFocus
            required
            value={email}
            maxLength={256}
            onChange={(event) => setEmail(event.target.value)}
          />
        </div>

        <div className="field">
          <label className="field-label" htmlFor="login-password">
            Passwort
          </label>
          <input
            id="login-password"
            type="password"
            className="input"
            autoComplete={firstAccount ? 'new-password' : 'current-password'}
            required
            minLength={firstAccount ? 8 : undefined}
            value={password}
            maxLength={128}
            onChange={(event) => setPassword(event.target.value)}
          />
          {firstAccount && <p className="field-note">Mindestens 8 Zeichen, mit Groß- und Kleinbuchstaben und einer Ziffer.</p>}
        </div>

        {error && (
          <p className="alert" role="alert">
            {error}
          </p>
        )}

        <button
          type="submit"
          className="button button-primary"
          disabled={busy || email.trim() === '' || password === ''}
        >
          {busy ? 'Einen Moment …' : firstAccount ? 'Konto anlegen' : 'Anmelden'}
        </button>

        <button
          type="button"
          className="button button-link"
          disabled={busy}
          onClick={() => {
            setFirstAccount(!firstAccount)
            setError(null)
          }}
        >
          {firstAccount ? 'Zurück zur Anmeldung' : 'Noch kein Konto? Erstes Konto anlegen'}
        </button>
      </form>
    </main>
  )
}
