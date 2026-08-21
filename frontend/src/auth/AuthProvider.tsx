import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'

import { apiFetch, type ApiFetchOptions } from '@/lib/api'
import { decodeToken } from '@/lib/jwt'

import { AuthContext, type AuthContextValue } from './auth-context'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'

/** Reads a still-valid token from storage, discarding anything unusable. */
function readStoredToken(): string | null {
  const stored = localStorage.getItem(TOKEN_STORAGE_KEY)

  if (!stored || !decodeToken(stored)) {
    // Missing, malformed, or expired — all equally useless to us.
    localStorage.removeItem(TOKEN_STORAGE_KEY)
    return null
  }

  return stored
}

export function AuthProvider({ children }: { children: ReactNode }) {
  // Read synchronously on first render so a refresh never flashes the login page.
  const [token, setToken] = useState<string | null>(readStoredToken)

  const user = useMemo(() => (token ? decodeToken(token) : null), [token])

  const signIn = useCallback((accessToken: string) => {
    localStorage.setItem(TOKEN_STORAGE_KEY, accessToken)
    setToken(accessToken)
  }, [])

  const signOut = useCallback(() => {
    localStorage.removeItem(TOKEN_STORAGE_KEY)
    setToken(null)
  }, [])

  // Sign out the moment the token expires, rather than waiting for the next
  // request to come back 401.
  useEffect(() => {
    if (!user) {
      return
    }

    const timer = setTimeout(signOut, user.expiresAt.getTime() - Date.now())
    return () => clearTimeout(timer)
  }, [user, signOut])

  const authFetch = useCallback(
    <T,>(path: string, options: Omit<ApiFetchOptions, 'token'> = {}) =>
      apiFetch<T>(path, { ...options, token }),
    [token],
  )

  const value = useMemo<AuthContextValue>(
    () => ({ user, token, isAuthenticated: user !== null, signIn, signOut, authFetch }),
    [user, token, signIn, signOut, authFetch],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
