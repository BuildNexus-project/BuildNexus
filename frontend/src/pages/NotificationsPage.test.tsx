import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import type { NotificationList, UserNotification } from '@/lib/notifications-api'
import { NotificationsPage } from '@/pages/NotificationsPage'
import { apiResponse } from '@/test/fake-fetch'
import { VILLA_ID } from '@/test/dashboard-fixtures'
import { signInAs } from '@/test/sign-in'

/**
 * The full notification history (US-26): every notification, newest first, a page at a time.
 *
 * The fake here behaves like the service — it pages by `skip` and `take`, remembers what was
 * marked read and answers `hasMore` — so "Load more" is tested against real paging rather than
 * against a canned second answer.
 */

type Call = { path: string; method: string }

/** `count` notifications, newest first, with the given ones (by position) already read. */
function history(count: number, read: number[] = []): UserNotification[] {
  return Array.from({ length: count }, (_, i) => ({
    id: `00000000-0000-4000-8000-${String(i).padStart(12, '0')}`,
    projectId: VILLA_ID,
    eventType: 'MilestoneCompleted' as const,
    message: `Milestone "M${i}" was completed on "Beachfront villa".`,
    // Position 0 is the newest.
    occurredAt: new Date(Date.UTC(2026, 8, 30, 12) - i * 3_600_000).toISOString(),
    isRead: read.includes(i),
  }))
}

function stubService(
  all: UserNotification[],
  options: { list?: Response | Error; page2?: Response | Error; mark?: Response | Error } = {},
): { calls: Call[]; state: { all: UserNotification[] } } {
  const calls: Call[] = []
  const state = { all: structuredClone(all) }

  vi.stubGlobal('fetch', (path: string, init: RequestInit = {}) => {
    const method = init.method ?? 'GET'
    calls.push({ path, method })

    if (method === 'GET') {
      const url = new URL(path, 'http://x')
      const skip = Number(url.searchParams.get('skip') ?? 0)
      const take = Number(url.searchParams.get('take') ?? 20)
      const override = skip === 0 ? options.list : options.page2

      if (override) {
        return override instanceof Error ? Promise.reject(override) : Promise.resolve(override)
      }

      const body: NotificationList = {
        unreadCount: state.all.filter((n) => !n.isRead).length,
        hasMore: state.all.length > skip + take,
        notifications: state.all.slice(skip, skip + take),
      }

      return Promise.resolve(apiResponse(200, body))
    }

    if (options.mark) {
      return options.mark instanceof Error ? Promise.reject(options.mark) : Promise.resolve(options.mark)
    }

    if (path.endsWith('/read-all')) {
      state.all = state.all.map((n) => ({ ...n, isRead: true }))
    } else {
      const id = path.split('/').at(-2)
      state.all = state.all.map((n) => (n.id === id ? { ...n, isRead: true } : n))
    }

    return Promise.resolve(apiResponse(204, null))
  })

  return { calls, state }
}

function renderPage() {
  signInAs('Client')

  render(
    <MemoryRouter>
      <AuthProvider>
        <NotificationsPage />
      </AuthProvider>
    </MemoryRouter>,
  )
}

/** The rows of the list, whatever the page. */
const rows = () => screen.getAllByRole('listitem')

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe('NotificationsPage', () => {
  describe('showing the history', () => {
    it('is headed Notifications and says what it holds', async () => {
      stubService(history(3))
      renderPage()

      expect(await screen.findByRole('heading', { level: 1, name: 'Notifications' })).toBeInTheDocument()
      expect(screen.getByText(/Every design approved, milestone completed and payment received/)).toBeInTheDocument()
    })

    it('opens with the newest twenty and asks for exactly that', async () => {
      const { calls } = stubService(history(45))
      renderPage()

      await screen.findAllByRole('listitem')

      expect(rows()).toHaveLength(20)
      expect(rows()[0]).toHaveTextContent('"M0"')
      expect(calls).toEqual([{ path: '/api/projects/notifications?skip=0&take=20', method: 'GET' }])
    })

    it('says how many are new, from the service’s whole figure', async () => {
      stubService(history(45, [0, 1, 2]))
      renderPage()

      // 45 notifications, three read: 42 new — though only twenty rows are showing.
      expect(await screen.findByText('42 new')).toBeInTheDocument()
    })

    it('does not show figures before the service has answered', () => {
      stubService(history(3))
      renderPage()

      expect(screen.getByText('Loading your notifications…')).toBeInTheDocument()
      expect(screen.queryByText(/^\d+ new$/)).not.toBeInTheDocument()
    })

    it('says so, and what will appear, when there is nothing yet', async () => {
      stubService([])
      renderPage()

      expect(await screen.findByText(/Nothing yet/)).toBeInTheDocument()
      expect(screen.queryByRole('listitem')).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument()
    })
  })

  describe('loading more', () => {
    it('offers it only while older notifications remain', async () => {
      stubService(history(20))
      renderPage()

      await screen.findAllByRole('listitem')

      // Exactly one full page and nothing after it.
      expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument()
    })

    it('adds the next page below the first, continuing from where it ended', async () => {
      const user = userEvent.setup()
      const { calls } = stubService(history(45))
      renderPage()

      await screen.findAllByRole('listitem')
      await user.click(screen.getByRole('button', { name: 'Load more' }))

      await screen.findByText(/"M20"/)
      expect(rows()).toHaveLength(40)
      expect(rows()[19]).toHaveTextContent('"M19"')
      expect(rows()[20]).toHaveTextContent('"M20"')
      expect(calls.at(-1)).toEqual({ path: '/api/projects/notifications?skip=20&take=20', method: 'GET' })
    })

    it('keeps going to the end, then stops offering more', async () => {
      const user = userEvent.setup()
      stubService(history(45))
      renderPage()

      await screen.findAllByRole('listitem')
      await user.click(screen.getByRole('button', { name: 'Load more' }))
      await screen.findByText(/"M20"/)
      await user.click(screen.getByRole('button', { name: 'Load more' }))

      await screen.findByText(/"M44"/)
      expect(rows()).toHaveLength(45)
      expect(screen.queryByRole('button', { name: 'Load more' })).not.toBeInTheDocument()
    })

    it('never shows the same notification twice if one arrived while paging', async () => {
      // A new notification pushes everything down one, so the next page begins with a row
      // already on screen.
      const user = userEvent.setup()
      const { state } = stubService(history(25))
      renderPage()
      await screen.findAllByRole('listitem')

      const arrived = history(1)[0]
      state.all = [{ ...arrived, id: 'ffffffff-0000-4000-8000-000000000001', message: 'Just arrived' }, ...state.all]
      await user.click(screen.getByRole('button', { name: 'Load more' }))

      await screen.findByText(/"M24"/)
      const messages = rows().map((row) => row.textContent)
      expect(new Set(messages).size).toBe(messages.length)
    })

    it('says so, and keeps what is showing, when the next page cannot be loaded', async () => {
      const user = userEvent.setup()
      stubService(history(45), { page2: apiResponse(502, { title: 'Bad Gateway' }) })
      renderPage()

      await screen.findAllByRole('listitem')
      await user.click(screen.getByRole('button', { name: 'Load more' }))

      expect(await screen.findByRole('alert')).toHaveTextContent('Bad Gateway')
      expect(rows()).toHaveLength(20)
      // And it can be tried again.
      expect(screen.getByRole('button', { name: 'Load more' })).toBeEnabled()
    })
  })

  describe('marking read', () => {
    it('marks one read in place, lowers the count, and keeps the pages already loaded', async () => {
      const user = userEvent.setup()
      const { calls } = stubService(history(45))
      renderPage()
      await screen.findAllByRole('listitem')
      await user.click(screen.getByRole('button', { name: 'Load more' }))
      await screen.findByText(/"M20"/)

      await user.click(within(rows()[0]).getByRole('button', { name: 'Mark as read' }))

      expect(await screen.findByText('44 new')).toBeInTheDocument()
      expect(within(rows()[0]).queryByRole('button', { name: 'Mark as read' })).not.toBeInTheDocument()
      // Not re-read: both pages are still there.
      expect(rows()).toHaveLength(40)
      expect(calls.filter((c) => c.method === 'GET')).toHaveLength(2)
      expect(calls.at(-1)).toEqual({
        path: '/api/projects/notifications/00000000-0000-4000-8000-000000000000/read',
        method: 'POST',
      })
    })

    it('marks them all read — including pages not loaded yet — and drops the badge and the button', async () => {
      const user = userEvent.setup()
      const { calls } = stubService(history(45))
      renderPage()
      await screen.findAllByRole('listitem')

      await user.click(screen.getByRole('button', { name: 'Mark all as read' }))

      await vi.waitFor(() => expect(screen.queryByText(/^\d+ new$/)).not.toBeInTheDocument())
      expect(screen.queryByRole('button', { name: 'Mark all as read' })).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Mark as read' })).not.toBeInTheDocument()
      expect(calls.at(-1)).toEqual({ path: '/api/projects/notifications/read-all', method: 'POST' })
      // Still listed — read is not deleted.
      expect(rows()).toHaveLength(20)
    })

    it('announces the change so the bell reads its own count again', async () => {
      const user = userEvent.setup()
      stubService(history(3))
      const changed = vi.fn()
      window.addEventListener('buildnexus:notifications-changed', changed)
      renderPage()
      await screen.findAllByRole('listitem')

      await user.click(within(rows()[0]).getByRole('button', { name: 'Mark as read' }))

      await vi.waitFor(() => expect(changed).toHaveBeenCalledTimes(1))
      window.removeEventListener('buildnexus:notifications-changed', changed)
    })

    it('says so, and leaves the screen as it was, when marking fails', async () => {
      const user = userEvent.setup()
      stubService(history(3), { mark: apiResponse(500, { title: 'Boom' }) })
      renderPage()
      await screen.findAllByRole('listitem')

      await user.click(within(rows()[0]).getByRole('button', { name: 'Mark as read' }))

      expect(await screen.findByRole('alert')).toHaveTextContent('Boom')
      expect(screen.getByText('3 new')).toBeInTheDocument()
      expect(within(rows()[0]).getByRole('button', { name: 'Mark as read' })).toBeInTheDocument()
    })

    it('does not tell the bell anything changed when marking fails', async () => {
      const user = userEvent.setup()
      stubService(history(3), { mark: new TypeError('Failed to fetch') })
      const changed = vi.fn()
      window.addEventListener('buildnexus:notifications-changed', changed)
      renderPage()
      await user.click(await screen.findByRole('button', { name: 'Mark all as read' }))

      expect(await screen.findByRole('alert')).toHaveTextContent('Your notifications could not be updated right now.')
      expect(changed).not.toHaveBeenCalled()
      window.removeEventListener('buildnexus:notifications-changed', changed)
    })
  })

  describe('when the service cannot be reached', () => {
    it('names what failed and shows no figure — never a reassuring zero', async () => {
      stubService(history(3), { list: apiResponse(502, { title: 'Bad Gateway' }) })
      renderPage()

      expect(await screen.findByRole('alert')).toHaveTextContent('Your notifications could not be loaded right now.')
      expect(screen.queryByText(/Nothing yet/)).not.toBeInTheDocument()
      expect(screen.queryByText(/^\d+ new$/)).not.toBeInTheDocument()
      expect(screen.queryByRole('listitem')).not.toBeInTheDocument()
    })

    it('gives the reason the service gave, when it gave one', async () => {
      stubService(history(3), {
        list: apiResponse(502, { title: 'Bad Gateway', detail: 'The project service is restarting.' }),
      })
      renderPage()

      expect(await screen.findByRole('alert')).toHaveTextContent('The project service is restarting.')
    })
  })
})
