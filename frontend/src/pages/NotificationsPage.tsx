import { useEffect, useState } from 'react'

import { useAuth } from '@/auth/auth-context'
import { SliceProblems } from '@/components/dashboard/SliceProblems'
import { NotificationRow } from '@/components/NotificationRow'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { ApiError, apiErrorMessage } from '@/lib/api'
import {
  fetchNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  type UserNotification,
} from '@/lib/notifications-api'
import { announceNotificationsChanged } from '@/lib/notifications-sync'
import type { SliceState } from '@/lib/use-dashboard-slice'

/** How many a visit to the page, and each "Load more", brings. */
const PAGE_SIZE = 20

const LOAD_FAILURE = 'Your notifications could not be loaded right now.'

/**
 * Every notification the signed-in Client or Architect has been sent, newest first (US-26) —
 * the full history behind the dashboard's panel, which shows only the latest few.
 *
 * Opened from the bell in the header or the panel's "View all". It reads a page when it opens and
 * another each time "Load more" is pressed, so a long history is never fetched in one go.
 *
 * Marking read updates what is on screen directly rather than reading the list again: pages
 * already loaded are not re-fetched, and a re-read of the first page would throw away the rest.
 * The service's answer to the change itself — success or not — is what decides whether the
 * screen changes, and it announces the change so the header's bell reads its own count again.
 */
export function NotificationsPage() {
  const { authFetch } = useAuth()
  const [first, setFirst] = useState<SliceState<null>>({ status: 'loading' })
  const [items, setItems] = useState<UserNotification[]>([])
  const [unread, setUnread] = useState(0)
  const [hasMore, setHasMore] = useState(false)
  const [loadingMore, setLoadingMore] = useState(false)
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false

    fetchNotifications(authFetch, { skip: 0, take: PAGE_SIZE })
      .then((list) => {
        if (!cancelled) {
          setItems(list.notifications)
          setUnread(list.unreadCount)
          setHasMore(list.hasMore)
          setFirst({ status: 'ready', data: null })
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setFirst({
            status: 'error',
            message: LOAD_FAILURE,
            detail: error instanceof ApiError ? error.detail : undefined,
          })
        }
      })

    return () => {
      cancelled = true
    }
  }, [authFetch])

  async function loadMore() {
    setLoadingMore(true)
    setFailure(null)

    try {
      // Carries on from however many are showing, so a page begins where the last one ended.
      const list = await fetchNotifications(authFetch, { skip: items.length, take: PAGE_SIZE })

      // A notification that arrived since the first page pushes everything down one, so the
      // next page can begin with one already on screen. Skipped by id, not shown twice.
      setItems((current) => {
        const known = new Set(current.map((n) => n.id))

        return [...current, ...list.notifications.filter((n) => !known.has(n.id))]
      })
      setUnread(list.unreadCount)
      setHasMore(list.hasMore)
    } catch (error) {
      setFailure(apiErrorMessage(error, 'More notifications could not be loaded right now.'))
    } finally {
      setLoadingMore(false)
    }
  }

  async function update(action: () => Promise<unknown>, apply: () => void) {
    setBusy(true)
    setFailure(null)

    try {
      await action()
      apply()
      announceNotificationsChanged()
    } catch (error) {
      setFailure(apiErrorMessage(error, 'Your notifications could not be updated right now.'))
    } finally {
      setBusy(false)
    }
  }

  function markRead(notification: UserNotification) {
    return update(
      () => markNotificationRead(authFetch, notification.id),
      () => {
        setItems((current) => current.map((n) => (n.id === notification.id ? { ...n, isRead: true } : n)))
        // Only an unread one lowers the count; it can be stale after a repeat click.
        setUnread((current) => (notification.isRead ? current : Math.max(0, current - 1)))
      },
    )
  }

  function markAllRead() {
    return update(
      () => markAllNotificationsRead(authFetch),
      () => {
        setItems((current) => current.map((n) => ({ ...n, isRead: true })))
        setUnread(0)
      },
    )
  }

  return (
    <main className="mx-auto flex w-full max-w-3xl flex-col gap-6 px-4 py-8 sm:px-6 sm:py-10">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex flex-col gap-1.5">
          <div className="flex items-center gap-2">
            <h1 className="font-heading text-2xl font-semibold tracking-tight">Notifications</h1>
            {unread > 0 && <Badge>{unread} new</Badge>}
          </div>
          <p className="text-muted-foreground text-sm text-pretty">
            Every design approved, milestone completed and payment received on your projects, newest
            first.
          </p>
        </div>

        {unread > 0 && (
          <Button variant="outline" size="sm" disabled={busy} onClick={() => void markAllRead()}>
            Mark all as read
          </Button>
        )}
      </div>

      <SliceProblems slices={[first]} />

      {failure && (
        <p role="alert" className="text-destructive text-sm">
          {failure}
        </p>
      )}

      {first.status === 'loading' && (
        <p className="text-muted-foreground text-sm">Loading your notifications…</p>
      )}

      {first.status === 'ready' && items.length === 0 && (
        <p className="text-muted-foreground bg-card ring-foreground/10 rounded-xl p-6 text-sm ring-1">
          Nothing yet. You will see an update here when a design is approved, a milestone is
          completed or a payment is received on one of your projects.
        </p>
      )}

      {items.length > 0 && (
        <ul className="divide-foreground/10 ring-foreground/10 bg-card flex flex-col divide-y rounded-xl ring-1">
          {items.map((notification) => (
            <NotificationRow
              key={notification.id}
              notification={notification}
              busy={busy}
              onMarkRead={() => void markRead(notification)}
            />
          ))}
        </ul>
      )}

      {hasMore && (
        <div className="flex justify-center">
          <Button variant="outline" disabled={loadingMore} onClick={() => void loadMore()}>
            {loadingMore ? 'Loading…' : 'Load more'}
          </Button>
        </div>
      )}
    </main>
  )
}
