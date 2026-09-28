import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'

import { AppShell } from '@/components/AppShell'

import { useAuth } from './auth-context'

/**
 * Gate for routes that need a signed-in user.
 *
 * Unauthenticated visitors are sent to `/login`, carrying where they were
 * headed so sign-in can return them there. This is a convenience, not a
 * security boundary — the data itself is protected by the User Service, which
 * rejects any request without a valid token.
 *
 * Every authenticated route is also framed by the <AppShell /> — the black
 * header with the role's own navigation, a breadcrumb strip, and the footer —
 * so no page needs its own. RoleRoute wraps this component, so role-gated pages
 * inherit the frame for free.
 */
export function ProtectedRoute({ children }: { children: ReactNode }) {
  const { isAuthenticated } = useAuth()
  const location = useLocation()

  if (!isAuthenticated) {
    return <Navigate to="/login" replace state={{ from: location }} />
  }

  return <AppShell>{children}</AppShell>
}