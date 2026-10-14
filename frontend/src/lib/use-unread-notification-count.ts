import { useCallback, useEffect, useState } from 'react'

import { useAuth } from '@/auth/auth-context'
import { fetchNotifications } from '@/lib/notifications-api'
import { useNotificationsChanged } from '@/lib/notifications-sync'
import { useAutoRefresh } from '@/lib/use-auto-refresh'

/**
 * How many notifications the signed-in person has not yet read, kept current without them doing
 * anything (US-26). `null` until it is known.
 *
 * Re-read on the same polling as the rest of the app (`useAutoRefresh`: on an interval while the
 * tab is visible, and the moment it becomes visible again), and straight away when something on
 * the page marks a notification read. Polling rather than a push channel, for the reason given on
 * `useAutoRefresh`: there is no push transport, and "the reader never has to trigger a refresh"
 * is what is needed.
 *
 * Asks for a page of one — the unread count in the answer is the whole figure whatever the page
 * size, so there is no reason to fetch a list to read a number.
 *
 * A failed read keeps the last count it had instead of replacing it with a guess. This is an
 * ambient figure in the header, not an answer the person asked for: an error here is not worth an
 * alert on every page, and the panel and history page, which are, say so themselves.
 */
export function useUnreadNotificationCount(): number | null {
  const { authFetch } = useAuth()
  const [count, setCount] = useState<number | null>(null)
  const [refresh, setRefresh] = useState(0)

  const reload = useCallback(() => setRefresh((current) => current + 1), [])

  useAutoRefresh(reload)
  useNotificationsChanged(reload)

  useEffect(() => {
    let cancelled = false

    fetchNotifications(authFetch, { take: 1 })
      .then((list) => {
        if (!cancelled) {
          setCount(list.unreadCount)
        }
      })
      .catch(() => {
        // Keep what we had: see above.
      })

    return () => {
      cancelled = true
    }
  }, [authFetch, refresh])

  return count
}
