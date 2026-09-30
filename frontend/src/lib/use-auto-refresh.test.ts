import { renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { DEFAULT_REFRESH_INTERVAL_MS, useAutoRefresh } from '@/lib/use-auto-refresh'

/**
 * Drives `document.visibilityState`, which is a read-only getter in jsdom, and
 * fires the event the browser would fire alongside it.
 */
function setVisibility(state: DocumentVisibilityState) {
  vi.spyOn(document, 'visibilityState', 'get').mockReturnValue(state)
  document.dispatchEvent(new Event('visibilitychange'))
}

describe('useAutoRefresh', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it('does not refresh on mount, because the caller already loads then', () => {
    // Firing here too would make every visit to the page fetch twice.
    const refresh = vi.fn()

    renderHook(() => useAutoRefresh(refresh, 1000))

    expect(refresh).not.toHaveBeenCalled()
  })

  it('refreshes on the interval while the tab is visible', () => {
    // US-13 AC-2: the Client sitting on the dashboard sees a status change
    // without doing anything, which the visibility trigger alone would miss.
    const refresh = vi.fn()

    renderHook(() => useAutoRefresh(refresh, 1000))

    vi.advanceTimersByTime(3000)

    expect(refresh).toHaveBeenCalledTimes(3)
  })

  it('stops polling while the tab is hidden', () => {
    // A forgotten background tab must not keep asking the Gateway for the same
    // rows all day.
    const refresh = vi.fn()

    renderHook(() => useAutoRefresh(refresh, 1000))

    setVisibility('hidden')
    refresh.mockClear()

    vi.advanceTimersByTime(10_000)

    expect(refresh).not.toHaveBeenCalled()
  })

  it('refreshes immediately when the tab becomes visible again', () => {
    // The common case: the Client leaves the dashboard open, the PM moves a
    // milestone, and the Client comes back to the tab.
    const refresh = vi.fn()

    renderHook(() => useAutoRefresh(refresh, 1000))

    setVisibility('hidden')
    refresh.mockClear()

    setVisibility('visible')

    expect(refresh).toHaveBeenCalledTimes(1)
  })

  it('resumes polling after the tab comes back', () => {
    const refresh = vi.fn()

    renderHook(() => useAutoRefresh(refresh, 1000))

    setVisibility('hidden')
    setVisibility('visible')
    refresh.mockClear()

    vi.advanceTimersByTime(2000)

    expect(refresh).toHaveBeenCalledTimes(2)
  })

  it('does not stack a second timer when the tab is shown twice', () => {
    // Two intervals running at once would double the request rate for the rest
    // of the page's life.
    const refresh = vi.fn()

    renderHook(() => useAutoRefresh(refresh, 1000))

    setVisibility('visible')
    setVisibility('visible')
    refresh.mockClear()

    vi.advanceTimersByTime(1000)

    expect(refresh).toHaveBeenCalledTimes(1)
  })

  it('stops polling once the page is unmounted', () => {
    // The page can be navigated away from mid-interval; a timer left running
    // would keep fetching for a screen nobody is looking at.
    const refresh = vi.fn()

    const { unmount } = renderHook(() => useAutoRefresh(refresh, 1000))

    unmount()
    vi.advanceTimersByTime(10_000)

    expect(refresh).not.toHaveBeenCalled()
  })

  it('polls every 30 seconds by default', () => {
    const refresh = vi.fn()

    renderHook(() => useAutoRefresh(refresh))

    vi.advanceTimersByTime(DEFAULT_REFRESH_INTERVAL_MS - 1)
    expect(refresh).not.toHaveBeenCalled()

    vi.advanceTimersByTime(1)
    expect(refresh).toHaveBeenCalledTimes(1)
  })
})
