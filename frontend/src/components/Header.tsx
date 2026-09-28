import { LogOut, Menu, X } from 'lucide-react'
import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { Button } from '@/components/ui/button'
import { initialsOf } from '@/lib/initials'
import { DASHBOARD_ITEM, NAV_ITEMS, activeNavItem, type NavItem } from '@/lib/nav'
import { ROLE_LABELS } from '@/lib/roles'
import { cn } from '@/lib/utils'

/**
 * Sticky app header rendered above every authenticated page, in the BuildNexus
 * black with the white line-art mark — the same identity as the landing page
 * banner. Brand on the left (always a way back to the dashboard), the signed-in
 * role's own navigation beside it with the current page marked, and on the right
 * who is signed in, a link to their profile, and Sign out.
 *
 * On a narrow screen the navigation folds into a menu button so nothing has to
 * wrap or scroll sideways.
 *
 * Rendered from `AppShell`, which `ProtectedRoute` wraps every authenticated
 * route in, so no page needs its own header. Returns null when there is no user:
 * the auth gate should have redirected to /login before this is asked to render,
 * so this is belt-and-braces for the frame right before that redirect commits.
 */
export function Header() {
  const { user, signOut } = useAuth()
  const { pathname } = useLocation()
  const [openAt, setOpenAt] = useState<string | null>(null)

  if (!user) {
    return null
  }

  // The menu is open for the page it was opened on, so navigating closes it
  // without an effect to reset it.
  const menuOpen = openAt === pathname
  const items = [DASHBOARD_ITEM, ...NAV_ITEMS[user.role]]
  const current = activeNavItem(user.role, pathname)

  return (
    <header className="sticky top-0 z-40 border-b border-white/10 bg-brand text-brand-foreground">
      <div className="mx-auto flex h-16 w-full max-w-7xl items-center gap-4 px-4 sm:px-6">
        <Link
          to="/home"
          className="flex shrink-0 items-center gap-2.5 rounded-lg outline-none focus-visible:ring-3 focus-visible:ring-white/40"
          aria-label="BuildNexus home"
        >
          {/* Black line-art on transparent, forced to white for the black bar. */}
          <img src="/logo-mark.png" alt="" className="h-9 w-auto brightness-0 invert" />
          <span className="font-heading text-base font-semibold tracking-tight">BuildNexus</span>
        </Link>

        <nav aria-label="Primary" className="ml-4 hidden items-center gap-1 md:flex">
          {items.map((item) => (
            <NavLink key={item.to} item={item} active={current?.to === item.to} />
          ))}
        </nav>

        <div className="ml-auto flex items-center gap-1.5">
          <Link
            to="/profile"
            aria-label="Your profile"
            className="flex items-center gap-2.5 rounded-lg px-2 py-1.5 outline-none hover:bg-white/10 focus-visible:ring-3 focus-visible:ring-white/40"
          >
            <span
              aria-hidden
              className="grid size-8 shrink-0 place-items-center rounded-full bg-brand-foreground text-xs font-semibold text-brand"
            >
              {initialsOf(user.fullName)}
            </span>
            <span className="hidden text-left leading-tight lg:block">
              <span className="block text-sm font-medium">{user.fullName}</span>
              <span className="block text-xs text-brand-foreground/60">
                {ROLE_LABELS[user.role]}
              </span>
            </span>
          </Link>

          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="hidden h-8 gap-1.5 px-2.5 text-brand-foreground hover:bg-white/10 hover:text-brand-foreground sm:inline-flex"
            onClick={signOut}
          >
            <LogOut />
            Sign out
          </Button>

          <Button
            type="button"
            variant="ghost"
            size="icon"
            className="text-brand-foreground hover:bg-white/10 hover:text-brand-foreground aria-expanded:bg-white/15 aria-expanded:text-brand-foreground md:hidden"
            aria-expanded={menuOpen}
            aria-controls="mobile-nav"
            aria-label={menuOpen ? 'Close menu' : 'Open menu'}
            onClick={() => setOpenAt(menuOpen ? null : pathname)}
          >
            {menuOpen ? <X /> : <Menu />}
          </Button>
        </div>
      </div>

      {menuOpen && (
        <div id="mobile-nav" className="border-t border-white/10 md:hidden">
          <nav aria-label="Mobile" className="mx-auto flex max-w-7xl flex-col gap-1 px-4 py-3">
            {items.map((item) => (
              <NavLink key={item.to} item={item} active={current?.to === item.to} block />
            ))}

            <div className="mt-2 flex items-center justify-between gap-3 border-t border-white/10 pt-3">
              <span className="min-w-0 text-sm leading-tight">
                <span className="block truncate font-medium">{user.fullName}</span>
                <span className="block text-xs text-brand-foreground/60">
                  {ROLE_LABELS[user.role]}
                </span>
              </span>
              <Button
                type="button"
                variant="ghost"
                size="sm"
                className="h-8 shrink-0 gap-1.5 px-2.5 text-brand-foreground hover:bg-white/10 hover:text-brand-foreground"
                onClick={signOut}
              >
                <LogOut />
                Sign out
              </Button>
            </div>
          </nav>
        </div>
      )}
    </header>
  )
}

function NavLink({
  item,
  active,
  block = false,
}: {
  item: NavItem
  active: boolean
  block?: boolean
}) {
  const Icon = item.icon

  return (
    <Link
      to={item.to}
      aria-current={active ? 'page' : undefined}
      className={cn(
        'flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium outline-none transition-colors focus-visible:ring-3 focus-visible:ring-white/40',
        block ? 'w-full' : 'h-9',
        active
          ? 'bg-white/15 text-brand-foreground'
          : 'text-brand-foreground/70 hover:bg-white/10 hover:text-brand-foreground',
      )}
    >
      <Icon className="size-4" aria-hidden />
      {item.label}
    </Link>
  )
}
