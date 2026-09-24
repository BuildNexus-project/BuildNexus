import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Button } from '@/components/ui/button'
import { ROLE_LABELS } from '@/lib/roles'

/**
 * Sticky app header rendered above every authenticated page. Left side is the
 * "BuildNexus" brand (a link back to /home, so the header is always a way
 * home); right side shows the signed-in user's name, their role, and a Sign
 * out button. Rendered from ProtectedRoute so every protected page picks it
 * up without a per-page edit.
 *
 * Returns null when there is no user — the auth gate should have redirected
 * to /login before this component is asked to render, so this is
 * belt-and-braces for the render frame right before that redirect commits.
 */
export function Header() {
  const { user, signOut } = useAuth()

  if (!user) {
    return null
  }

  return (
    <header className="bg-background sticky top-0 z-40 flex items-center justify-between border-b px-6 py-3">
      {/* Brand — always returns the user to the home page, mirroring the
          SLIIT CourseWeb pattern where the top-left logo is a home link. */}
      <Link
        to="/home"
        className="text-lg font-semibold tracking-tight hover:opacity-80"
        aria-label="BuildNexus home"
      >
        BuildNexus
      </Link>

      {/* Right-hand cluster: name, role, sign-out — stacked, right-aligned so
          the name and role read from the top down and the button sits under
          them exactly as the reference layout shows. */}
      <div className="flex flex-col items-end gap-1">
        <span className="text-sm font-medium leading-tight">{user.fullName}</span>
        <span className="text-muted-foreground text-xs leading-tight">
          {ROLE_LABELS[user.role]}
        </span>
        <Button
          type="button"
          variant="outline"
          size="sm"
          className="mt-1 h-7 px-3 text-xs"
          onClick={signOut}
        >
          Sign out
        </Button>
      </div>
    </header>
  )
}