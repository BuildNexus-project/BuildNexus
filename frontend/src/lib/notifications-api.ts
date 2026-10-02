import type { ApiFetchOptions } from './api'

/** The token-attaching fetch handed out by the auth context. */
type AuthFetch = <T>(path: string, options?: Omit<ApiFetchOptions, 'token'>) => Promise<T>

/*
 * In-app notifications (US-26).
 *
 * Notifications are written by the Project Service from events the other services already
 * publish, and are only ever read and dismissed from here. All three endpoints are for a Client
 * or an Architect and none takes a user id: whose notifications these are is always the
 * signed-in account's own.
 */

/** The three events that produce a notification. */
export type NotificationEventType = 'DesignApproved' | 'MilestoneCompleted' | 'PaymentReceived'

/**
 * Named for what it is in this app rather than `Notification`, which is the browser's own
 * global and would silently shadow it.
 */
export type UserNotification = {
  id: string
  /** The project it is about, for linking to it. */
  projectId: string
  eventType: NotificationEventType
  /** The finished sentence. */
  message: string
  /** ISO-8601, in UTC with a `Z`: when the event happened. */
  occurredAt: string
  isRead: boolean
}

export type NotificationList = {
  /**
   * Every notification not yet read — not only the ones in `notifications`. The list is capped by
   * the service and this is not, so "12 new" is true even when 10 are shown.
   */
  unreadCount: number
  /** Newest first, read and unread alike. */
  notifications: UserNotification[]
}

export function fetchNotifications(authFetch: AuthFetch) {
  return authFetch<NotificationList>('/api/projects/notifications')
}

/** Marks one read. Repeating it is harmless. */
export function markNotificationRead(authFetch: AuthFetch, id: string) {
  return authFetch<null>(`/api/projects/notifications/${id}/read`, { method: 'POST' })
}

/** Marks everything of the signed-in account's read. */
export function markAllNotificationsRead(authFetch: AuthFetch) {
  return authFetch<null>('/api/projects/notifications/read-all', { method: 'POST' })
}
