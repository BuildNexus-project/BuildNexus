import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'

import { useAuth } from './auth-context'

/**
 * Gate for routes that need a signed-in user.
 *
 * Unauthenticated visitors are sent to `/login`, carrying where they were
 * headed so sign-in can return them there. This is a convenience, not a
 * security boundary — the data itself is protected by the User Service, which
 * rejects any request without a valid token.
 */
export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated } = useAuth()
  const location = useLocation()

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location }} />
  }

  return <>{children}</>
}
