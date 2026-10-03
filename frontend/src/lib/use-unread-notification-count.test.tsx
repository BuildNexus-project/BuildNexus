import { act, renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { announceNotificationsChanged } from '@/lib/notifications-sync'
import { DEFAULT_REFRESH_INTERVAL_MS } from '@/lib/use-auto-refresh'
import { useUnreadNotificationCount } from '@/lib/use-unread-notification-count'
import { apiResponse } from '@/test/fake-fetch'
import { signInAs } from '@/test/sign-in'

/**
 * The number behind the header's bell (US-26): read when it appears, kept current by polling,
 * and read again the moment something on the page marks a notification read.
 */
function wrapper({ children }: { children: ReactNode }) {
  return (
    <MemoryRouter>
      <AuthProvider>{children}</AuthProvider>
    </MemoryRouter>
  )
}

/** A notifications endpoint whose unread figure the test can change, remembering what was asked. */
function stubUnread(initial: number) {
  const state = { unread: initial, paths: [] as string[], fail: false }

  vi.stubGlobal('fetch', (path: string) => {
    state.paths.push(path)

    return Promise.resolve(
      state.fail
        ? apiResponse(502, { title: 'Bad Gateway' })
        : apiResponse(200, { unreadCount: state.unread, hasMore: false, notifications: [] }),
    )
  })

  return state
}

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe('useUnreadNotificationCount', () => {
  it('is not known until the service has answered — never a zero in the meantime', () => {
    signInAs('Client')
    stubUnread(3)

    const { result } = renderHook(() => useUnreadNotificationCount(), { wrapper })

    expect(result.current).toBeNull()
  })

  it('reads the unread figure, asking for a page of one rather than a list', async () => {
    signInAs('Client')
    const service = stubUnread(7)

    const { result } = renderHook(() => useUnreadNotificationCount(), { wrapper })

    await waitFor(() => expect(result.current).toBe(7))
    expect(service.paths).toEqual(['/api/projects/notifications?take=1'])
  })

  it('reads again when something on the page announces a change', async () => {
    signInAs('Client')
    const service = stubUnread(3)
    const { result } = renderHook(() => useUnreadNotificationCount(), { wrapper })
    await waitFor(() => expect(result.current).toBe(3))

    service.unread = 0
    act(() => announceNotificationsChanged())

    await waitFor(() => expect(result.current).toBe(0))
  })

  it('reads again on the usual polling interval while the tab is visible', async () => {
    signInAs('Client')
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval'] })
    const service = stubUnread(1)
    const { result } = renderHook(() => useUnreadNotificationCount(), { wrapper })

    // Not `waitFor`: it polls on the very timers faked here. Let the first read settle by hand.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })
    expect(result.current).toBe(1)

    service.unread = 4
    await act(async () => {
      await vi.advanceTimersByTimeAsync(DEFAULT_REFRESH_INTERVAL_MS)
    })

    expect(result.current).toBe(4)
  })

  it('keeps the last count it had when a later read fails, instead of guessing', async () => {
    signInAs('Client')
    const service = stubUnread(5)
    const { result } = renderHook(() => useUnreadNotificationCount(), { wrapper })
    await waitFor(() => expect(result.current).toBe(5))

    service.fail = true
    const before = service.paths.length
    act(() => announceNotificationsChanged())
    await waitFor(() => expect(service.paths.length).toBe(before + 1))

    expect(result.current).toBe(5)
  })

  it('stays unknown, and does not throw, if the very first read fails', async () => {
    signInAs('Client')
    const service = stubUnread(5)
    service.fail = true

    const { result } = renderHook(() => useUnreadNotificationCount(), { wrapper })
    await waitFor(() => expect(service.paths).toHaveLength(1))

    expect(result.current).toBeNull()
  })
})
