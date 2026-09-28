import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { DASHBOARD_ITEM, NAV_ITEMS } from '@/lib/nav'
import { ROLE_LABELS } from '@/lib/roles'

/**
 * The app footer, in the same black as the header so the two frame every
 * authenticated page: the mark and what BuildNexus is, the current role's own
 * links (the same ones as its header, so a page that scrolls a long way still
 * has somewhere to go at the bottom), and the account.
 *
 * Returns null without a user, like the header.
 */
export function Footer() {
  const { user, signOut } = useAuth()

  if (!user) {
    return null
  }

  const links = [DASHBOARD_ITEM, ...NAV_ITEMS[user.role]]

  return (
    <footer className="mt-auto bg-brand text-brand-foreground">
      <div className="mx-auto grid w-full max-w-7xl gap-8 px-4 py-10 sm:px-6 md:grid-cols-[1.4fr_1fr_1fr]">
        <div className="max-w-sm">
          <div className="flex items-center gap-2.5">
            <img src="/logo-mark.png" alt="" className="h-9 w-auto brightness-0 invert" />
            <span className="font-heading text-base font-semibold tracking-tight">BuildNexus</span>
          </div>
          <p className="mt-3 text-sm text-brand-foreground/60">
            Construction project management, end to end — design, build and payment working from
            the same record.
          </p>
        </div>

        <nav aria-label="Footer">
          <h2 className="text-xs font-medium tracking-wide text-brand-foreground/50 uppercase">
            {ROLE_LABELS[user.role]} workspace
          </h2>
          <ul className="mt-3 flex flex-col gap-2 text-sm">
            {links.map((item) => (
              <li key={item.to}>
                <Link
                  to={item.to}
                  className="text-brand-foreground/80 underline-offset-4 hover:text-brand-foreground hover:underline"
                >
                  {item.label}
                </Link>
              </li>
            ))}
          </ul>
        </nav>

        <div>
          <h2 className="text-xs font-medium tracking-wide text-brand-foreground/50 uppercase">
            Account
          </h2>
          <ul className="mt-3 flex flex-col gap-2 text-sm">
            <li className="text-brand-foreground/80">
              Signed in as <span className="text-brand-foreground">{user.fullName}</span>
            </li>
            <li>
              <Link
                to="/profile"
                className="text-brand-foreground/80 underline-offset-4 hover:text-brand-foreground hover:underline"
              >
                Your profile
              </Link>
            </li>
            <li>
              <button
                type="button"
                onClick={signOut}
                className="cursor-pointer text-brand-foreground/80 underline-offset-4 hover:text-brand-foreground hover:underline"
              >
                Sign out
              </button>
            </li>
          </ul>
        </div>
      </div>

      <div className="border-t border-white/10">
        <div className="mx-auto flex w-full max-w-7xl flex-col gap-1 px-4 py-4 text-xs text-brand-foreground/50 sm:flex-row sm:items-center sm:justify-between sm:px-6">
          <span>© {new Date().getFullYear()} BuildNexus. All rights reserved.</span>
          <span>Access is scoped to your role, in the app and in the services.</span>
        </div>
      </div>
    </footer>
  )
}
