import { useState, type FormEvent } from 'react'
import { changePassword } from '../../api/users'
import { useAuth } from '../../auth/authContext'

// Eigenes Passwort ändern. Das Backend prüft das bisherige Passwort und die Regeln für das neue.
// Danach sind alle ANDEREN Geräte abgemeldet, dieses bleibt angemeldet.
export function PasswordForm() {
  const { user } = useAuth()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [repeat, setRepeat] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  async function handleSubmit(event: FormEvent) {
    event.preventDefault()
    setError(null)
    setNotice(null)
    // Tippfehler im neuen Passwort schon hier abfangen, das Backend sieht die Wiederholung nicht.
    if (next !== repeat) {
      setError('Die beiden neuen Passwörter sind nicht gleich.')
      return
    }

    setBusy(true)
    try {
      await changePassword(current, next)
      setNotice('Passwort geändert. Andere Geräte sind jetzt abgemeldet.')
      setCurrent('')
      setNext('')
      setRepeat('')
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <section className="tile settings-card" aria-labelledby="settings-password">
      <h2 id="settings-password" className="settings-title">
        Mein Passwort
      </h2>
      {error !== null && (
        <p className="alert" role="alert">
          {error}
        </p>
      )}
      {notice !== null && (
        <p className="settings-notice" role="status">
          {notice}
        </p>
      )}
      <form className="settings-form" onSubmit={handleSubmit}>
        {/* Unsichtbares Benutzerfeld: Passwort-Manager wissen so, zu welchem Konto das Passwort gehört. */}
        <input type="email" autoComplete="username" value={user?.email ?? ''} readOnly hidden />
        <div className="field">
          <label className="field-label" htmlFor="password-current">
            Bisheriges Passwort
          </label>
          <input
            id="password-current"
            type="password"
            className="input"
            required
            maxLength={128}
            autoComplete="current-password"
            value={current}
            onChange={(event) => setCurrent(event.target.value)}
          />
        </div>
        <div className="field">
          <label className="field-label" htmlFor="password-new">
            Neues Passwort
          </label>
          <input
            id="password-new"
            type="password"
            className="input"
            required
            maxLength={128}
            autoComplete="new-password"
            value={next}
            onChange={(event) => setNext(event.target.value)}
          />
        </div>
        <div className="field">
          <label className="field-label" htmlFor="password-repeat">
            Neues Passwort wiederholen
          </label>
          <input
            id="password-repeat"
            type="password"
            className="input"
            required
            maxLength={128}
            autoComplete="new-password"
            value={repeat}
            onChange={(event) => setRepeat(event.target.value)}
          />
        </div>
        <button type="submit" className="button button-primary" disabled={busy}>
          {busy ? 'Wird geändert …' : 'Passwort ändern'}
        </button>
      </form>
    </section>
  )
}
