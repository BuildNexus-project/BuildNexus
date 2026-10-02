import { Banknote, FileCheck, Flag, type LucideIcon } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'

import { useAuth } from '@/auth/auth-context'
import { DashboardPanel } from '@/components/dashboard/DashboardPanel'
import { SliceProblems } from '@/components/dashboard/SliceProblems'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { ApiError, apiErrorMessage } from '@/lib/api'
import {
  fetchNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  type NotificationEventType,
  type NotificationList,
  type UserNotification,
} from '@/lib/notifications-api'
import type { SliceState } from '@/lib/use-dashboard-slice'
import { cn } from '@/lib/utils'

const LOAD_FAILURE = 'Your notifications could not be loaded right now.'

/** What each kind of notification looks like: an icon that says which, beside the sentence. */
const ICONS: Record<NotificationEventType, LucideIcon> = {
  DesignApproved: FileCheck,
  MilestoneCompleted: Flag,
  PaymentReceived: Banknote,
}

/** When it happened, in the viewer's own time zone — the service sends UTC with a `Z`. */
function formatWhen(iso: string): string {
  return new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
}

/**
 * The signed-in Client's or Architect's notifications (US-26 AC-2): what has happened on their
 * projects since they last looked — a design approved, a milestone completed, a payment received.
 *
 * Passive by design. Nothing is pushed: the notifications were stored when the events happened,
 * and they appear here the next time the dashboard loads, which is also what a login lands on.
 * It is its own request to the Project Service and fails on its own, so a service that is down
 * leaves a sentence saying so beside a dashboard that is otherwise unaffected.
 *
 * Marking read asks the service and then reads the list again, rather than patching the list in
 * place: the service's answer is the truth, and the "N new" figure is its whole count, which a
 * capped list cannot be used to recompute.
 */
export function NotificationsPanel() {
  const { authFetch } = useAuth()
  const [notifications, setNotifications] = useState<SliceState<NotificationList>>({ status: 'loading' })
  // Bumped after a successful change, which is what reads the list again.
  const [refresh, setRefresh] = useState(0)
  const [busy, setBusy] = useState(false)
  const [updateFailure, setUpdateFailure] = useState<string | null>(null)

  // Not `useDashboardSlice`, which reads once on mount: this one has to read again after a change.
  // The previous list stays on screen while the next is fetched, so marking one read does not
  // blank the panel for the length of a round trip.
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
      setRefresh((current) => current + 1)
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

function NotificationRow({
  notification,
  busy,
  onMarkRead,
}: {
  notification: UserNotification
  busy: boolean
  onMarkRead: () => void
}) {
  const Icon = ICONS[notification.eventType]

  return (
    <li className="flex flex-col gap-3 p-4 sm:flex-row sm:items-start sm:justify-between">
      <div className="flex min-w-0 gap-3">
        <span
          className={cn(
            'grid size-9 shrink-0 place-items-center rounded-lg',
            notification.isRead ? 'bg-muted text-muted-foreground' : 'bg-brand text-brand-foreground',
          )}
        >
          <Icon className="size-4" aria-hidden />
        </span>

        <div className="min-w-0">
          <p className={cn('text-sm text-pretty break-words', !notification.isRead && 'font-medium')}>
            {!notification.isRead && <span className="sr-only">Unread: </span>}
            {notification.message}
          </p>
          <p className="text-muted-foreground mt-1 flex flex-wrap gap-x-3 text-xs">
            <time dateTime={notification.occurredAt}>{formatWhen(notification.occurredAt)}</time>
            <Link to={`/projects/${notification.projectId}`} className="underline underline-offset-4">
              View project
            </Link>
          </p>
        </div>
      </div>

      {!notification.isRead && (
        <Button variant="ghost" size="sm" disabled={busy} onClick={onMarkRead} className="self-start">
          Mark as read
        </Button>
      )}
    </li>
  )
}
