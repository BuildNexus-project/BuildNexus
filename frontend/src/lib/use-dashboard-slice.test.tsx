import { renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { apiResponse, stubFetch } from '@/test/fake-fetch'
import { signInAs } from '@/test/sign-in'

import { useDashboardSlice } from './use-dashboard-slice'

/**
 * One slice of a dashboard loading on its own (US-21). The real `apiFetch` sits under
 * it, so the problem-details parsing is exercised rather than mocked past: what a
 * service says when it refuses is the thing the person is shown.
 */
const wrapper = ({ children }: { children: ReactNode }) => <AuthProvider>{children}</AuthProvider>

const load = <T,>(authFetch: <R>(path: string) => Promise<R>) => authFetch<T>('/api/anything')

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('useDashboardSlice', () => {
  it('is loading until the service has answered', () => {
    signInAs('Client')
    stubFetch(apiResponse(200, { some: 'slice' }))

    const { result } = renderHook(() => useDashboardSlice(load), { wrapper })

    expect(result.current).toEqual({ status: 'loading' })
  })

  it('is ready with the slice once it arrives', async () => {
    signInAs('Client')
    stubFetch(apiResponse(200, { some: 'slice' }))

    const { result } = renderHook(() => useDashboardSlice(load), { wrapper })

    await waitFor(() => expect(result.current.status).toBe('ready'))
    expect(result.current).toEqual({ status: 'ready', data: { some: 'slice' } })
  })

  it('keeps the reason a service gave when it refuses, beside its own wording', async () => {
    // The Design Service answers 502 when it cannot ask the Project Service who the
    // Client is. The reason has to reach the person, not "something went wrong" — and the
    // message still names the slice, so a dashboard of several says which one failed.
    signInAs('Client')
    stubFetch(
      apiResponse(502, { title: 'Projects could not be listed', detail: 'Try again in a moment.' }),
    )

    const { result } = renderHook(() => useDashboardSlice(load, 'Design status could not be loaded.'), {
      wrapper,
    })

    await waitFor(() => expect(result.current.status).toBe('error'))
    expect(result.current).toEqual({
      status: 'error',
      message: 'Design status could not be loaded.',
      detail: 'Try again in a moment.',
    })
  })

  it('does not treat a problems title as a reason', async () => {
    // A title labels the kind of failure ("Internal Server Error"); shown as the reason it
    // would say nothing the slice's own message has not.
    signInAs('Client')
    stubFetch(apiResponse(500, { title: 'Internal Server Error' }))

    const { result } = renderHook(() => useDashboardSlice(load, 'Payments due could not be loaded.'), {
      wrapper,
    })

    await waitFor(() => expect(result.current.status).toBe('error'))
    expect(result.current).toEqual({
      status: 'error',
      message: 'Payments due could not be loaded.',
      detail: undefined,
    })
  })

  it('uses the default wording when the failure was not a service answer', async () => {
    signInAs('Client')
    stubFetch(new Error('connection refused'))

    const { result } = renderHook(() => useDashboardSlice(load), { wrapper })

    await waitFor(() => expect(result.current.status).toBe('error'))
    expect(result.current).toEqual({
      status: 'error',
      message: 'This could not be loaded right now.',
      detail: undefined,
    })
  })

  it('uses the callers own wording for a failure that was not a service answer', async () => {
    signInAs('Client')
    stubFetch(new Error('connection refused'))

    const { result } = renderHook(() => useDashboardSlice(load, 'Design status is unavailable.'), {
      wrapper,
    })

    await waitFor(() => expect(result.current.status).toBe('error'))
    expect(result.current).toEqual({
      status: 'error',
      message: 'Design status is unavailable.',
      detail: undefined,
    })
  })

  it('asks for the slice once, not on every render', async () => {
    signInAs('Client')
    const requests = stubFetch(apiResponse(200, { some: 'slice' }))

    const { result, rerender } = renderHook(() => useDashboardSlice(load), { wrapper })

    await waitFor(() => expect(result.current.status).toBe('ready'))
    rerender()
    rerender()

    expect(requests).toHaveLength(1)
  })

  it('does not report an answer that arrives after the page has gone', async () => {
    signInAs('Client')
    stubFetch(apiResponse(200, { some: 'slice' }))
    const errors = vi.spyOn(console, 'error').mockImplementation(() => undefined)

    const { unmount } = renderHook(() => useDashboardSlice(load), { wrapper })
    unmount()

    // Let the answer land after the unmount; a state update on an unmounted component
    // would be logged.
    await new Promise((resolve) => setTimeout(resolve, 0))

    expect(errors).not.toHaveBeenCalled()
    errors.mockRestore()
  })
})
