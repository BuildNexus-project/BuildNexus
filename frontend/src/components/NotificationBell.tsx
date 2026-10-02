import { Bell } from 'lucide-react'
import { Link } from 'react-router-dom'

import { useUnreadNotificationCount } from '@/lib/use-unread-notification-count'

/** The most the badge shows. A bell that says "1,284" is no more useful than one that says "99+". */
const BADGE_CEILING = 99

/**
 * The bell in the header (US-26): a way to the full notification history from any page, with how
 * many are unread.
 *
 * The count keeps itself current — see `useUnreadNotificationCount` — so a notification that
 * arrives while someone is working shows up here without a reload. It shows nothing at all when
 * there is nothing unread, and also before the first answer arrives or if it never does: no
 * badge is never a claim that nothing is new, only that nothing is known to be.
 *
 * Only rendered for the roles that are ever sent notifications, so a Project Manager or Admin
 * is not shown a bell that could only ever be empty, nor asked for a count.
 */
export function NotificationBell() {
  const unread = useUnreadNotificationCount()
  const hasUnread = unread !== null && unread > 0

  return (
    <Link
      to="/notifications"
      aria-label={hasUnread ? `Notifications, ${unread} unread` : 'Notifications'}
      className="relative grid size-9 shrink-0 place-items-center rounded-lg text-brand-foreground/80 outline-none transition-colors hover:bg-white/10 hover:text-brand-foreground focus-visible:ring-3 focus-visible:ring-white/40"
    >
      <Bell className="size-5" aria-hidden />

      {hasUnread && (
        <span
          aria-hidden
          className="absolute -top-0.5 -right-0.5 grid h-4 min-w-4 place-items-center rounded-full bg-brand-foreground px-1 text-[10px] leading-none font-semibold text-brand tabular-nums"
        >
          {unread > BADGE_CEILING ? `${BADGE_CEILING}+` : unread}
        </span>
      )}
    </Link>
  )
}
