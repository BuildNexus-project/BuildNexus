import type { ReactNode } from 'react'

import { AccessDenied } from '@/components/AccessDenied'
import type { Role } from '@/lib/roles'

import { ProtectedRoute } from './ProtectedRoute'
import { useAuth } from './auth-context'

/**
 * Gate for routes only some roles may open — `<ProtectedRoute>` plus a role
 * check.
 *
 * A visitor who is not signed in is sent to `/login` exactly as before. One who
 * is signed in but holds the wrong role stays where they are and is told so,
 * rather than being redirected somewhere that hides what happened.
 *
 * Like `<ProtectedRoute>`, this is a convenience and not a security boundary:
 * it keeps a user out of a page they cannot use, but the endpoints behind it
 * enforce the same roles themselves and answer 403 whatever the browser does.
 */
export function RoleRoute({
  allowedRoles,
  children,
}: {
  allowedRoles: readonly Role[]
  children: ReactNode
}) {
  return (
    <ProtectedRoute>
      <RoleGate allowedRoles={allowedRoles}>{children}</RoleGate>
    </ProtectedRoute>
  )
}

/** Only ever rendered inside `<ProtectedRoute>`, so there is a user by here. */
function RoleGate({ allowedRoles, children }: { allowedRoles: readonly Role[]; children: ReactNode }) {
  const { user } = useAuth()

  if (!user) {
    return null
  }

  if (!allowedRoles.includes(user.role)) {
    return <AccessDenied role={user.role} allowedRoles={allowedRoles} />
  }

  return <>{children}</>
}
