import { useEffect, useState, type FormEvent } from 'react'
import type { Role } from '../../api/auth'
import { changeRole, createUser, deleteUser, getUsers, type UserListItem } from '../../api/users'
import { useAuth } from '../../auth/authContext'
import { PlusIcon, TrashIcon } from '../Icons'
import { LoadingMessage } from '../StateMessages'

const dateFormat = new Intl.DateTimeFormat('de-DE', { dateStyle: 'medium' })

// Benutzer verwalten: Liste mit Rolle, neuen Benutzer anlegen, Rolle ändern, löschen.
// Das eigene Konto lässt sich hier nicht löschen oder herabstufen, und der letzte Admin bleibt immer.
// Beides prüft das Backend (409), die Oberfläche sperrt die Knöpfe nur zusätzlich.
export function UserAdmin() {
  const { user: me } = useAuth()
  const [users, setUsers] = useState<UserListItem[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [confirmDelete, setConfirmDelete] = useState<string | null>(null)
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [role, setRole] = useState<Role>('User')

  function load() {
    getUsers()
      .then((loaded) => {
        setUsers(loaded)
        setError(null)
      })
      .catch((err: Error) => setError(err.message))
  }

  useEffect(() => {
    load()
  }, [])

  // Gemeinsamer Ablauf für alle Änderungen: Knöpfe sperren, ausführen, Liste neu laden, Meldung zeigen.
  async function run(action: () => Promise<void>, success: string) {
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await action()
      setNotice(success)
      load()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
      setConfirmDelete(null)
    }
  }

  async function handleCreate(event: FormEvent) {
    event.preventDefault()
    await run(() => createUser(email.trim(), password, role), `${email.trim()} wurde angelegt.`)
    setEmail('')
    setPassword('')
    setRole('User')
  }

  const adminCount = users?.filter((u) => u.role === 'Admin').length ?? 0

  return (
    <section className="tile settings-card settings-wide" aria-labelledby="settings-users">
      <h2 id="settings-users" className="settings-title">
        Benutzer
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

      {users === null ? (
        error === null && <LoadingMessage text="Benutzer werden geladen …" />
      ) : (
        <div className="table-scroll" tabIndex={0} aria-label="Benutzerliste">
          <table className="settings-table">
            <thead>
              <tr>
                <th scope="col">E-Mail</th>
                <th scope="col">Rolle</th>
                <th scope="col" className="hide-small">
                  Angelegt
                </th>
                <th scope="col">
                  <span className="visually-hidden">Aktionen</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {users.map((u) => {
                const isMe = u.email === me?.email
                // Der letzte Admin darf nicht herabgestuft oder gelöscht werden.
                const locked = isMe || (u.role === 'Admin' && adminCount <= 1)
                return (
                  <tr key={u.id}>
                    <td>
                      {u.email}
                      {isMe && <span className="settings-badge">du</span>}
                      {u.lockedOut && <span className="settings-badge settings-badge-warning">gesperrt</span>}
                    </td>
                    <td>
                      <label className="visually-hidden" htmlFor={`role-${u.id}`}>
                        Rolle von {u.email}
                      </label>
                      <select
                        id={`role-${u.id}`}
                        className="input input-compact"
                        value={u.role}
                        disabled={busy || locked}
                        title={locked ? 'Eigenes Konto oder letzter Admin' : undefined}
                        onChange={(event) =>
                          void run(() => changeRole(u.id, event.target.value as Role), `Rolle von ${u.email} geändert.`)
                        }
                      >
                        <option value="Admin">Admin</option>
                        <option value="User">Nur lesen</option>
                      </select>
                    </td>
                    <td className="settings-muted hide-small">{dateFormat.format(new Date(u.createdAtUtc))}</td>
                    <td className="settings-actions">
                      {confirmDelete === u.id ? (
                        <>
                          <button
                            type="button"
                            className="button button-danger"
                            disabled={busy}
                            onClick={() => void run(() => deleteUser(u.id), `${u.email} wurde gelöscht.`)}
                          >
                            Ja, löschen
                          </button>
                          <button type="button" className="button" disabled={busy} onClick={() => setConfirmDelete(null)}>
                            Abbrechen
                          </button>
                        </>
                      ) : (
                        <button
                          type="button"
                          className="button button-icon"
                          disabled={busy || locked}
                          aria-label={`${u.email} löschen`}
                          title={locked ? 'Eigenes Konto oder letzter Admin' : 'Löschen'}
                          onClick={() => setConfirmDelete(u.id)}
                        >
                          <TrashIcon size={20} />
                        </button>
                      )}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}

      <form className="settings-form" onSubmit={handleCreate}>
        <h3 className="settings-subtitle">Neuen Benutzer anlegen</h3>
        <div className="settings-form-row">
          <div className="field">
            <label className="field-label" htmlFor="new-user-email">
              E-Mail
            </label>
            <input
              id="new-user-email"
              type="email"
              className="input"
              required
              maxLength={256}
              autoComplete="off"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
          </div>
          <div className="field">
            <label className="field-label" htmlFor="new-user-password">
              Startpasswort
            </label>
            <input
              id="new-user-password"
              type="password"
              className="input"
              required
              maxLength={128}
              autoComplete="new-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
            />
          </div>
          <div className="field">
            <label className="field-label" htmlFor="new-user-role">
              Rolle
            </label>
            <select
              id="new-user-role"
              className="input"
              value={role}
              onChange={(event) => setRole(event.target.value as Role)}
            >
              <option value="User">Nur lesen</option>
              <option value="Admin">Admin</option>
            </select>
          </div>
          <button type="submit" className="button button-primary" disabled={busy}>
            <PlusIcon size={20} />
            Anlegen
          </button>
        </div>
        <p className="settings-muted">Passwort: mindestens 8 Zeichen, Groß- und Kleinbuchstabe und eine Ziffer.</p>
      </form>
    </section>
  )
}
