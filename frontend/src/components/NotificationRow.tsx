import { Banknote, FileCheck, Flag, type LucideIcon } from 'lucide-react'
import { Link } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import type { NotificationEventType, UserNotification } from '@/lib/notifications-api'
import { cn } from '@/lib/utils'

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
 * One notification (US-26): the sentence, when it happened, a link to its project, and — while
 * it is unread — a button to mark it read.
 *
 * Shared by the dashboard's panel and the full history page, so the two cannot drift into
 * showing the same notification differently. An unread one is set apart by more than colour: a
 * bolder sentence, a dark icon, and "Unread:" for a screen reader.
 *
 * Renders an `li`, so it belongs inside a list.
 */
export function NotificationRow({
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
