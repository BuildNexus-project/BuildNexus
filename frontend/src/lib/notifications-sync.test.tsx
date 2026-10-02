import { renderHook } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { announceNotificationsChanged, useNotificationsChanged } from './notifications-sync'

/** How the header's bell learns that the panel or the history page has marked something read. */
describe('notifications-sync', () => {
  it('tells a listener that notifications changed', () => {
    const onChange = vi.fn()
    renderHook(() => useNotificationsChanged(onChange))

    announceNotificationsChanged()

    expect(onChange).toHaveBeenCalledTimes(1)
  })

  it('tells every listener, so several parts of the page can follow one change', () => {
    const first = vi.fn()
    const second = vi.fn()
    renderHook(() => useNotificationsChanged(first))
    renderHook(() => useNotificationsChanged(second))

    announceNotificationsChanged()

    expect(first).toHaveBeenCalledTimes(1)
    expect(second).toHaveBeenCalledTimes(1)
  })

  it('stops telling a listener once it has unmounted', () => {
    const onChange = vi.fn()
    const { unmount } = renderHook(() => useNotificationsChanged(onChange))

    unmount()
    announceNotificationsChanged()

    expect(onChange).not.toHaveBeenCalled()
  })

  it('does nothing when nobody is listening', () => {
    expect(() => announceNotificationsChanged()).not.toThrow()
  })
})
