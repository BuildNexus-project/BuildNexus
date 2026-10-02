import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { NotificationsPanel } from '@/components/dashboard/NotificationsPanel'
import type { NotificationList } from '@/lib/notifications-api'
import { apiResponse } from '@/test/fake-fetch'
import { NOTIFICATIONS_PATH, VILLA_ID, notificationList } from '@/test/dashboard-fixtures'
import { signInAs } from '@/test/sign-in'

/**
 * The notifications panel (US-26 AC-2): what a Client or an Architect sees of the events that
 * happened on their projects since they last looked.
 *
 * Marking read asks the service and then reads the list again, so the fake here behaves like the
 * service — it remembers what was marked — rather than answering the same list every time.
 */

type Call = { path: string; method: string }

/**
 * Stands in for the Project Service's notifications endpoints. `mark` decides what a marking
 * request answers with; by default it succeeds and the list changes to match.
 */
function stubService(
  initial: NotificationList = notificationList(),
  options: { list?: Response | Error; mark?: Response | Error } = {},
): Call[] {
  const calls: Call[] = []
  let current = structuredClone(initial)

  vi.stubGlobal('fetch', (path: string, init: RequestInit = {}) => {
    const method = init.method ?? 'GET'
    calls.push({ path, method })

    if (path === NOTIFICATIONS_PATH && method === 'GET') {
      const answer = options.list ?? apiResponse(200, current)
      return answer instanceof Error ? Promise.reject(answer) : Promise.resolve(answer)
    }

    if (options.mark) {
      return options.mark instanceof Error ? Promise.reject(options.mark) : Promise.resolve(options.mark)
    }

    if (path === `${NOTIFICATIONS_PATH}/read-all`) {
      current = {
        unreadCount: 0,
        hasMore: false,
        notifications: current.notifications.map((n) => ({ ...n, isRead: true })),
      }
    } else if (path.endsWith('/read')) {
      const id = path.split('/').at(-2)
      const wasUnread = current.notifications.some((n) => n.id === id && !n.isRead)
      current = {
        unreadCount: current.unreadCount - (wasUnread ? 1 : 0),
        hasMore: current.hasMore,
        notifications: current.notifications.map((n) => (n.id === id ? { ...n, isRead: true } : n)),
      }
    }

    return Promise.resolve(apiResponse(204, null))
  })

  return calls
}

function renderPanel() {
  signInAs('Client')

  render(
    <MemoryRouter>
      <AuthProvider>
        <NotificationsPanel />
      </AuthProvider>
    </MemoryRouter>,
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe('NotificationsPanel', () => {
  describe('showing what has been stored', () => {
    it('lists each notification’s sentence, newest first', async () => {
      stubService()
      renderPanel()

      const items = await screen.findAllByRole('listitem')

      expect(items).toHaveLength(3)
      expect(items[0]).toHaveTextContent('A payment was received on "Beachfront villa".')
      expect(items[1]).toHaveTextContent('Milestone "Foundation" was completed on "Beachfront villa".')
      expect(items[2]).toHaveTextContent('Design "Ground floor plan" (version 2) was approved on "Beachfront villa".')
    })

    it('says how many are new, in the heading', async () => {
      stubService()
      renderPanel()

      expect(await screen.findByText('2 new')).toBeInTheDocument()
    })

    it('shows the service’s whole unread figure, not the length of the capped list', async () => {
      // Twelve unread in all, only two of them in the list the service sent.
      stubService(notificationList(10))
      renderPanel()

      expect(await screen.findByText('12 new')).toBeInTheDocument()
    })

    it('tells a screen reader which are unread, and does not for one already read', async () => {
      stubService()
      renderPanel()

      const [payment, , design] = await screen.findAllByRole('listitem')

      expect(within(payment).getByText('Unread:')).toBeInTheDocument()
      expect(within(design).queryByText('Unread:')).not.toBeInTheDocument()
    })

    it('offers a link from each notification to its project', async () => {
      stubService()
      renderPanel()

      const [payment] = await screen.findAllByRole('listitem')

      expect(within(payment).getByRole('link', { name: 'View project' })).toHaveAttribute(
        'href',
        `/projects/${VILLA_ID}`,
      )
    })

    it('shows when it happened, as a machine-readable time as well', async () => {
      stubService()
      renderPanel()

      const [payment] = await screen.findAllByRole('listitem')
      const time = payment.querySelector('time')

      expect(time).toHaveAttribute('datetime', '2026-09-20T11:00:00.000Z')
      expect(time?.textContent).toMatch(/2026/)
    })

    it('asks the Project Service for the signed-in person’s notifications and nothing else', async () => {
      const calls = stubService()
      renderPanel()

      await screen.findAllByRole('listitem')

      expect(calls).toEqual([{ path: NOTIFICATIONS_PATH, method: 'GET' }])
    })

    it('does not show figures before the service has answered', () => {
      stubService()
      renderPanel()

      // Synchronously after render nothing has been answered: a placeholder, not "0 new".
      expect(screen.getByText('Loading your notifications…')).toBeInTheDocument()
      expect(screen.queryByText(/^d+ new$/)).not.toBeInTheDocument()
    })
  })

  describe('when there is nothing to show', () => {
    it('says so, and says what will appear', async () => {
      stubService({ unreadCount: 0, hasMore: false, notifications: [] })
      renderPanel()

      expect(await screen.findByText('Nothing new')).toBeInTheDocument()
      expect(screen.getByText(/design is approved, a milestone is completed or a payment is received/)).toBeInTheDocument()
      expect(screen.queryByRole('button', { name: 'Mark all as read' })).not.toBeInTheDocument()
      expect(screen.queryByText(/^d+ new$/)).not.toBeInTheDocument()
    })

    it('shows read notifications without a badge or any mark-read control', async () => {
      const allRead = notificationList()
      stubService({
        unreadCount: 0,
        hasMore: false,
        notifications: allRead.notifications.map((n) => ({ ...n, isRead: true })),
      })
      renderPanel()

      expect(await screen.findAllByRole('listitem')).toHaveLength(3)
      expect(screen.queryByText(/^d+ new$/)).not.toBeInTheDocument()
      expect(screen.queryByRole('button', { name: /mark/i })).not.toBeInTheDocument()
    })
  })

  describe('marking read', () => {
    it('marks one read, and the list and the count follow', async () => {
      const user = userEvent.setup()
      const calls = stubService()
      renderPanel()

      const [payment] = await screen.findAllByRole('listitem')
      await user.click(within(payment).getByRole('button', { name: 'Mark as read' }))

      expect(await screen.findByText('1 new')).toBeInTheDocument()
      expect(calls).toContainEqual({
        path: `${NOTIFICATIONS_PATH}/aaaaaaaa-0000-4000-8000-000000000003/read`,
        method: 'POST',
      })
      expect(within((await screen.findAllByRole('listitem'))[0]).queryByRole('button')).not.toBeInTheDocument()
    })

    it('offers the control only on the unread ones', async () => {
      stubService()
      renderPanel()

      const items = await screen.findAllByRole('listitem')

      expect(within(items[0]).getByRole('button', { name: 'Mark as read' })).toBeInTheDocument()
      expect(within(items[1]).getByRole('button', { name: 'Mark as read' })).toBeInTheDocument()
      expect(within(items[2]).queryByRole('button')).not.toBeInTheDocument()
    })

    it('marks them all read, and the badge and the button go', async () => {
      const user = userEvent.setup()
      const calls = stubService()
      renderPanel()

      await user.click(await screen.findByRole('button', { name: 'Mark all as read' }))

      await waitFor(() => expect(screen.queryByText(/^d+ new$/)).not.toBeInTheDocument())
      expect(screen.queryByRole('button', { name: 'Mark all as read' })).not.toBeInTheDocument()
      expect(calls).toContainEqual({ path: `${NOTIFICATIONS_PATH}/read-all`, method: 'POST' })
      // Still listed — read is not deleted.
      expect(screen.getAllByRole('listitem')).toHaveLength(3)
    })

    it('says so, and keeps the list as it was, when marking fails', async () => {
      const user = userEvent.setup()
      stubService(notificationList(), { mark: apiResponse(500, { title: 'Boom' }) })
      renderPanel()

      const [payment] = await screen.findAllByRole('listitem')
      await user.click(within(payment).getByRole('button', { name: 'Mark as read' }))

      expect(await screen.findByRole('alert')).toHaveTextContent('Boom')
      expect(screen.getByText('2 new')).toBeInTheDocument()
      expect(screen.getAllByRole('listitem')).toHaveLength(3)
    })

    it('says so when marking fails with no answer at all', async () => {
      const user = userEvent.setup()
      stubService(notificationList(), { mark: new TypeError('Failed to fetch') })
      renderPanel()

      await user.click(await screen.findByRole('button', { name: 'Mark all as read' }))

      expect(await screen.findByRole('alert')).toHaveTextContent(
        'Your notifications could not be updated right now.',
      )
    })
  })

  describe('the rest of the history', () => {
    it('leads to the full list from a link beside the heading', async () => {
      stubService()
      renderPanel()

      await screen.findAllByRole('listitem')

      expect(screen.getByRole('link', { name: 'View all' })).toHaveAttribute('href', '/notifications')
    })

    it('offers the link even when there is nothing yet, so the page is always findable from here', async () => {
      stubService({ unreadCount: 0, hasMore: false, notifications: [] })
      renderPanel()

      await screen.findByText('Nothing new')

      expect(screen.getByRole('link', { name: 'View all' })).toBeInTheDocument()
    })
  })

  describe('staying current', () => {
    it('shows a notification that arrives while the dashboard is open, without a reload', async () => {
      // The reader sits on the dashboard; the tab is hidden, then shown again — the same trigger
      // the rest of the app refreshes on. Nothing is clicked.
      const calls = stubService({ unreadCount: 0, hasMore: false, notifications: [] })
      renderPanel()
      await screen.findByText('Nothing new')

      stubService(notificationList())
      document.dispatchEvent(new Event('visibilitychange'))

      expect(await screen.findByText('A payment was received on "Beachfront villa".')).toBeInTheDocument()
      expect(screen.getByText('2 new')).toBeInTheDocument()
      expect(calls.length).toBeGreaterThan(0)
    })

    it('keeps the list on screen while the next one is fetched', async () => {
      stubService()
      renderPanel()
      await screen.findAllByRole('listitem')

      document.dispatchEvent(new Event('visibilitychange'))

      // Not blanked to "Loading…" for the length of a round trip.
      expect(screen.queryByText('Loading your notifications…')).not.toBeInTheDocument()
      expect(screen.getAllByRole('listitem')).toHaveLength(3)
    })

    it('announces a change, so the bell in the header reads its own count again', async () => {
      const user = userEvent.setup()
      stubService()
      const changed = vi.fn()
      window.addEventListener('buildnexus:notifications-changed', changed)
      renderPanel()

      const [payment] = await screen.findAllByRole('listitem')
      await user.click(within(payment).getByRole('button', { name: 'Mark as read' }))

      await waitFor(() => expect(changed).toHaveBeenCalledTimes(1))
      window.removeEventListener('buildnexus:notifications-changed', changed)
    })

    it('does not announce anything when marking fails', async () => {
      const user = userEvent.setup()
      stubService(notificationList(), { mark: apiResponse(500, { title: 'Boom' }) })
      const changed = vi.fn()
      window.addEventListener('buildnexus:notifications-changed', changed)
      renderPanel()

      await user.click(await screen.findByRole('button', { name: 'Mark all as read' }))
      await screen.findByRole('alert')

      expect(changed).not.toHaveBeenCalled()
      window.removeEventListener('buildnexus:notifications-changed', changed)
    })
  })

  describe('when the service cannot be reached', () => {
    it('names what failed and shows no figure — never a reassuring zero', async () => {
      stubService(notificationList(), { list: apiResponse(502, { title: 'Bad Gateway' }) })
      renderPanel()

      expect(await screen.findByRole('alert')).toHaveTextContent(
        'Your notifications could not be loaded right now.',
      )
      expect(screen.queryByText('Nothing new')).not.toBeInTheDocument()
      expect(screen.queryByText(/^d+ new$/)).not.toBeInTheDocument()
      expect(screen.queryByRole('listitem')).not.toBeInTheDocument()
    })

    it('gives the reason the service gave, when it gave one', async () => {
      stubService(notificationList(), {
        list: apiResponse(502, { title: 'Bad Gateway', detail: 'The project service is restarting.' }),
      })
      renderPanel()

      expect(await screen.findByRole('alert')).toHaveTextContent('The project service is restarting.')
    })
  })
})
