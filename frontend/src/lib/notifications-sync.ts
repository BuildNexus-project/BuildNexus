import { useEffect } from 'react'

/**
 * How one part of the page tells another that the person's notifications have changed (US-26).
 *
 * The header's bell shows the unread count and the panel or history page is where a notification
 * is marked read. They are in different corners of the page and neither owns the other, so the
 * one that changes something announces it on `window` and the other re-reads. Without this the
 * bell would go on saying "3 new" for up to the length of its poll after the last was read.
 */
const NOTIFICATIONS_CHANGED = 'buildnexus:notifications-changed'

/** Says that something was marked read, so anything showing a count should read it again. */
export function announceNotificationsChanged(): void {
  window.dispatchEvent(new Event(NOTIFICATIONS_CHANGED))
}

/**
 * Calls `onChange` whenever {@link announceNotificationsChanged} is called.
 *
 * `onChange` must be referentially stable — wrap it in `useCallback` — or the listener is torn
 * down and put back on every render.
 */
export function useNotificationsChanged(onChange: () => void): void {
  useEffect(() => {
    window.addEventListener(NOTIFICATIONS_CHANGED, onChange)

    return () => window.removeEventListener(NOTIFICATIONS_CHANGED, onChange)
  }, [onChange])
}
