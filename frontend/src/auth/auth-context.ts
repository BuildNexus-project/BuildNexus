import { createContext, useContext } from 'react'

import type { ApiFetchOptions } from '@/lib/api'
import type { AuthUser } from '@/lib/jwt'

export type AuthContextValue = {
  /** The signed-in user, or `null` when nobody is signed in. */
  user: AuthUser | null
  token: string | null
  isAuthenticated: boolean
  /** Records a token returned by the login endpoint. */
  signIn: (accessToken: string) => void
  signOut: () => void
  /** Calls the API with the current token attached. */
  authFetch: <T>(path: string, options?: Omit<ApiFetchOptions, 'token'>) => Promise<T>
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)

  if (!context) {
    throw new Error('useAuth must be used inside an <AuthProvider>.')
  }

  return context
}
