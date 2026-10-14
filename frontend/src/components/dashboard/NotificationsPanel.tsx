import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { DashboardPanel } from '@/components/dashboard/DashboardPanel'
import { SliceProblems } from '@/components/dashboard/SliceProblems'
import { NotificationRow } from '@/components/NotificationRow'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { ApiError, apiErrorMessage } from '@/lib/api'
import {
  fetchNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  type NotificationList,
} from '@/lib/notifications-api'
import { announceNotificationsChanged } from '@/lib/notifications-sync'
import { useAutoRefresh } from '@/lib/use-auto-refresh'
import type { SliceState } from '@/lib/use-dashboard-slice'

const LOAD_FAILURE = 'Your notifications could not be loaded right now.'

/**
 * The signed-in Client's or Architect's notifications (US-26 AC-2): what has happened on their
 * projects since they last looked — a design approved, a milestone completed, a payment received.
 *
 * Nothing is pushed: the notifications were stored when the events happened, and they appear
 * here the next time the dashboard loads, which is also what a login lands on. Left open, it
 * reads again on the app's usual polling, so a new one turns up without a reload. It is its own
 * request to the Project Service and fails on its own, so a service that is down leaves a
 * sentence saying so beside a dashboard that is otherwise unaffected.
 *
 * Marking read asks the service and then reads the list again, rather than patching the list in
 * place: the service's answer is the truth, and the "N new" figure is its whole count, which a
 * short list cannot be used to recompute. It also announces the change, so the bell in the
 * header reads its own count again straight away.
 *
 * Only the newest page is shown; "View all" leads to the full history.
 */
export function NotificationsPanel() {
  const { authFetch } = useAuth()
  const [notifications, setNotifications] = useState<SliceState<NotificationList>>({ status: 'loading' })
  // Bumped after a successful change, which is what reads the list again.
  const [refresh, setRefresh] = useState(0)
  const [busy, setBusy] = useState(false)
  const [updateFailure, setUpdateFailure] = useState<string | null>(null)

  const reload = useCallback(() => setRefresh((current) => current + 1), [])
  useAutoRefresh(reload)

  // Not `useDashboardSlice`, which reads once on mount: this one has to read again after a change
  // and on a timer. The previous list stays on screen while the next is fetched, so marking one
  // read, or a poll, does not blank the panel for the length of a round trip.
  useEffect(() => {
    let cancelled = false

    fetchNotifications(authFetch)
      .then((data) => {
        if (!cancelled) {
          setNotifications({ status: 'ready', data })
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setNotifications({
            status: 'error',
            message: LOAD_FAILURE,
            detail: error instanceof ApiError ? error.detail : undefined,
          })
        }
      })

    // The effect can outlive the page if the user navigates away mid-request.
    return () => {
      cancelled = true
    }
  }, [authFetch, refresh])

  async function update(action: () => Promise<unknown>) {
    setBusy(true)
    setUpdateFailure(null)

    try {
      await action()
      reload()
      announceNotificationsChanged()
    } catch (error) {
      setUpdateFailure(apiErrorMessage(error, 'Your notifications could not be updated right now.'))
    } finally {
      setBusy(false)
    }
  }

  const unread = notifications.status === 'ready' ? notifications.data.unreadCount : 0

  return (
    <section aria-labelledby="notifications-heading" className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <h2 id="notifications-heading" className="font-heading text-lg font-semibold tracking-tight">
            Notifications
          </h2>
          {unread > 0 && <Badge>{unread} new</Badge>}
        </div>

        <div className="flex items-center gap-3">
          <Link to="/notifications" className="text-sm underline underline-offset-4">
            View all
          </Link>

          {unread > 0 && (
            <Button
              variant="outline"
              size="sm"
              disabled={busy}
              onClick={() => void update(() => markAllNotificationsRead(authFetch))}
            >
              Mark all as read
            </Button>
          )}
        </div>
      </div>

      <SliceProblems slices={[notifications]} />

      {updateFailure && (
        <p role="alert" className="text-destructive text-sm">
          {updateFailure}
        </p>
      )}

      {notifications.status === 'loading' && (
        <p className="text-muted-foreground text-sm">Loading your notifications…</p>
      )}

      {notifications.status === 'ready' && notifications.data.notifications.length === 0 && (
        <DashboardPanel title="Nothing new">
          <p className="text-muted-foreground text-sm">
            You will see an update here when a design is approved, a milestone is completed or a
            payment is received on one of your projects.
          </p>
        </DashboardPanel>
      )}

      {notifications.status === 'ready' && notifications.data.notifications.length > 0 && (
        <ul className="divide-foreground/10 ring-foreground/10 bg-card flex flex-col divide-y rounded-xl ring-1">
          {notifications.data.notifications.map((notification) => (
            <NotificationRow
              key={notification.id}
              notification={notification}
              busy={busy}
              onMarkRead={() => void update(() => markNotificationRead(authFetch, notification.id))}
            />
          ))}
        </ul>
      )}
    </section>
  )
}
