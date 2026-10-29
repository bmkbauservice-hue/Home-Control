import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { login, logout, register, restoreSession, type AuthUser } from '../api/auth'
import { onSessionEnded } from '../api/tokenStore'
import { AuthContext, type AuthContextValue, type AuthStatus } from './authContext'

type AuthState = { status: AuthStatus; user: AuthUser | null }

const anonymous: AuthState = { status: 'anonymous', user: null }

// Hält fest, wer angemeldet ist, und stellt es der ganzen App bereit (Context API).
export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: 'loading', user: null })

  useEffect(() => {
    let ignore = false

    // StrictMode führt diesen Effekt im Development doppelt aus. Das ist harmlos, weil
    // refreshAccessToken() nur einen Refresh gleichzeitig zulässt (siehe api/tokenStore.ts).
    restoreSession().then((user) => {
      if (!ignore) setState(user ? { status: 'authenticated', user } : anonymous)
    })

    // Lehnt der Server einen Refresh ab, ist die Sitzung zu Ende: zurück zur Anmeldeseite.
    const stopListening = onSessionEnded(() => setState(anonymous))

    return () => {
      ignore = true
      stopListening()
    }
  }, [])

  const signIn = useCallback(async (email: string, password: string) => {
    const user = await login(email, password)
    setState({ status: 'authenticated', user })
  }, [])

  const setUpFirstAccount = useCallback(
    async (email: string, password: string) => {
      await register(email, password)
      await signIn(email, password)
    },
    [signIn],
  )

  const signOut = useCallback(async () => {
    try {
      await logout()
    } finally {
      setState(anonymous)
    }
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({
      status: state.status,
      user: state.user,
      isAdmin: state.user?.role === 'Admin',
      signIn,
      setUpFirstAccount,
      signOut,
    }),
    [state, signIn, setUpFirstAccount, signOut],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
