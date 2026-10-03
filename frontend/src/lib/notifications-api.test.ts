import { afterEach, describe, expect, it, vi } from 'vitest'

import { ApiError, apiFetch } from './api'
import {
  fetchNotifications,
  markAllNotificationsRead,
  markNotificationRead,
  type NotificationList,
} from './notifications-api'
import { apiResponse, stubFetch } from '@/test/fake-fetch'

/**
 * The notification clients (US-26). These go through the real `apiFetch`, so the request shape,
 * the 204-with-no-body handling and the `ApiError` mapping are exercised rather than mocked past.
 */
const authFetch = <T,>(path: string, options = {}) => apiFetch<T>(path, options)

afterEach(() => {
  vi.unstubAllGlobals()
})

const LIST: NotificationList = {
  unreadCount: 12,
  hasMore: true,
  notifications: [
    {
      id: '7b0f4b0e-0000-4000-8000-000000000001',
      projectId: 'e059b652-129d-4a69-a4ab-e5e95ed4b543',
      eventType: 'MilestoneCompleted',
      message: 'Milestone "Foundation" was completed on "Beachfront villa".',
      occurredAt: '2026-09-20T11:00:00.000Z',
      isRead: false,
    },
  ],
}

describe('fetchNotifications', () => {
  it('reads the list from the project service, with no id of any kind', async () => {
    const requests = stubFetch(apiResponse(200, LIST))

    await fetchNotifications(authFetch)

    expect(requests).toHaveLength(1)
    expect(requests[0].path).toBe('/api/projects/notifications')
    expect(requests[0].method).toBeUndefined()
  })

  it('returns the list and the unread count exactly as the service sent them', async () => {
    stubFetch(apiResponse(200, LIST))

    const list = await fetchNotifications(authFetch)

    expect(list).toEqual(LIST)
    // More unread than are listed: the count is the whole figure, not the length of the list.
    expect(list.unreadCount).toBeGreaterThan(list.notifications.length)
  })

  it('throws an ApiError carrying the status when the service refuses', async () => {
    stubFetch(apiResponse(403, { title: 'Not allowed for your role', detail: 'Contact an administrator.' }))

    const error = await fetchNotifications(authFetch).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(403)
    expect((error as ApiError).detail).toBe('Contact an administrator.')
  })
})

describe('markNotificationRead', () => {
  it('posts to the notification’s own read path', async () => {
    const requests = stubFetch(apiResponse(204, null))

    await markNotificationRead(authFetch, 'abc-123')

    expect(requests).toHaveLength(1)
    expect(requests[0].path).toBe('/api/projects/notifications/abc-123/read')
    expect(requests[0].method).toBe('POST')
  })

  it('resolves when the service answers 204 with no body', async () => {
    // A 204 has an empty body; apiFetch must not try to parse one.
    vi.stubGlobal('fetch', () =>
      Promise.resolve({ ok: true, status: 204, text: () => Promise.resolve('') } as unknown as Response),
    )

    await expect(markNotificationRead(authFetch, 'abc-123')).resolves.toBeNull()
  })

  it('throws an ApiError with the 404 when the notification is not the caller’s', async () => {
    stubFetch(apiResponse(404, { title: 'Not Found' }))

    const error = await markNotificationRead(authFetch, 'abc-123').catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(404)
  })
})

describe('fetchNotifications paging', () => {
  it('asks for a page by skip and take', async () => {
    const requests = stubFetch(apiResponse(200, LIST))

    await fetchNotifications(authFetch, { skip: 20, take: 10 })

    expect(requests[0].path).toBe('/api/projects/notifications?skip=20&take=10')
  })

  it('sends only what it is given, leaving the service its defaults for the rest', async () => {
    const requests = stubFetch(apiResponse(200, LIST), apiResponse(200, LIST))

    await fetchNotifications(authFetch, { take: 1 })
    await fetchNotifications(authFetch, { skip: 40 })

    expect(requests[0].path).toBe('/api/projects/notifications?take=1')
    expect(requests[1].path).toBe('/api/projects/notifications?skip=40')
  })

  it('sends a skip of zero rather than treating it as absent', async () => {
    const requests = stubFetch(apiResponse(200, LIST))

    await fetchNotifications(authFetch, { skip: 0, take: 20 })

    expect(requests[0].path).toBe('/api/projects/notifications?skip=0&take=20')
  })

  it('returns whether there are older notifications beyond the page', async () => {
    stubFetch(apiResponse(200, LIST))

    expect((await fetchNotifications(authFetch)).hasMore).toBe(true)
  })

  it('surfaces a 400 for a page out of range as an ApiError naming the field', async () => {
    stubFetch(apiResponse(400, { title: 'Invalid', errors: { take: ['take must be between 1 and 50.'] } }))

    const error = await fetchNotifications(authFetch, { take: 0 }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).fieldErrors.take).toEqual(['take must be between 1 and 50.'])
  })
})

describe('markAllNotificationsRead', () => {
  it('posts to the read-all path', async () => {
    const requests = stubFetch(apiResponse(204, null))

    await markAllNotificationsRead(authFetch)

    expect(requests).toHaveLength(1)
    expect(requests[0].path).toBe('/api/projects/notifications/read-all')
    expect(requests[0].method).toBe('POST')
    expect(requests[0].body).toBeUndefined()
  })

  it('throws an ApiError when the service is unavailable', async () => {
    stubFetch(apiResponse(502, { title: 'Bad Gateway' }))

    const error = await markAllNotificationsRead(authFetch).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(502)
  })
})
