import { useEffect } from 'react'

/**
 * How often a screen that is being watched re-reads its data, in milliseconds.
 *
 * Thirty seconds is a deliberate middle: a construction milestone moves a
 * handful of times over months, so a Client watching the dashboard is not
 * waiting on a number that changes by the second, and a slower poll would make
 * the page feel stale during the one moment it matters — the Client sitting on
 * it while the Project Manager marks something complete.
 */
export const DEFAULT_REFRESH_INTERVAL_MS = 30_000

/**
 * Keeps a screen's data current without the reader doing anything (US-13 AC-2).
 *
 * Two triggers, which between them cover both ways data goes stale:
 *
 * - **The tab becomes visible again.** The common case is a Client leaving the
 *   dashboard open, the PM moving a milestone, and the Client coming back to
 *   the tab hours later. This re-reads the moment they return, so what they see
 *   was fetched after they looked away.
 * - **A timer, while the tab is visible.** Covers the Client sitting *on* the
 *   dashboard as a status changes, which the visibility trigger alone would
 *   never catch. The timer is cleared whenever the tab is hidden and restarted
 *   when it comes back, so a forgotten background tab stops polling instead of
 *   asking the Gateway for the same rows all day.
 *
 * Polling rather than a push channel (SSE, WebSocket): the Construction Service
 * has no push transport today, and adding one is infrastructure well outside a
 * read-only story. What the AC asks for is that the reader never has to trigger
 * a refresh themselves, and this satisfies that honestly.
 *
 * `visibilitychange` is used rather than the window `focus` event on purpose.
 * It covers the same "came back to the tab" case, but does not also fire when
 * focus moves between two windows with the page still on screen — where the
 * timer is already running and a second fetch would be wasted. Listening to
 * both would double-fetch on every tab switch.
 *
 * @param refresh Called to re-read. Must be referentially stable — wrap it in
 *   `useCallback`, or the effect tears down and rebuilds on every render and
 *   the timer never reaches its interval.
 * @param intervalMs How often to poll while visible.
 */
export function useAutoRefresh(
  refresh: () => void,
  intervalMs: number = DEFAULT_REFRESH_INTERVAL_MS,
): void {
  useEffect(() => {
    let timer: ReturnType<typeof setInterval> | undefined

    function stopPolling() {
      if (timer !== undefined) {
        clearInterval(timer)
        timer = undefined
      }
    }

    function startPolling() {
      // Guard against stacking a second interval on top of a running one, which
      // would double the request rate for the rest of the page's life.
      stopPolling()
      timer = setInterval(refresh, intervalMs)
    }

    function onVisibilityChange() {
      if (document.visibilityState === 'visible') {
        refresh()
        startPolling()
        return
      }

      stopPolling()
    }

    // No refresh on mount here: the caller's own load runs then, and firing
    // both would make every visit to the page fetch twice.
    if (document.visibilityState === 'visible') {
      startPolling()
    }

    document.addEventListener('visibilitychange', onVisibilityChange)

    return () => {
      stopPolling()
      document.removeEventListener('visibilitychange', onVisibilityChange)
    }
  }, [refresh, intervalMs])
}
