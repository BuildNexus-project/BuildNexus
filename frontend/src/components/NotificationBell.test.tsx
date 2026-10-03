import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { NotificationBell } from '@/components/NotificationBell'
import { apiResponse } from '@/test/fake-fetch'
import { signInAs } from '@/test/sign-in'

/** The bell in the header (US-26): a way to the history, and how many are unread. */
function renderBell(answer: Response | Error) {
  signInAs('Client')
  vi.stubGlobal('fetch', () => (answer instanceof Error ? Promise.reject(answer) : Promise.resolve(answer)))

  render(
    <MemoryRouter>
      <AuthProvider>
        <NotificationBell />
      </AuthProvider>
    </MemoryRouter>,
  )
}

const unread = (unreadCount: number) => apiResponse(200, { unreadCount, hasMore: false, notifications: [] })

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe('NotificationBell', () => {
  it('leads to the full history', () => {
    renderBell(unread(0))

    expect(screen.getByRole('link', { name: 'Notifications' })).toHaveAttribute('href', '/notifications')
  })

  it('says how many are unread, to the eye and to a screen reader', async () => {
    renderBell(unread(3))

    const link = await screen.findByRole('link', { name: 'Notifications, 3 unread' })

    expect(link).toHaveTextContent('3')
  })

  it('shows no badge at all when nothing is unread', async () => {
    renderBell(unread(0))

    await screen.findByRole('link', { name: 'Notifications' })

    expect(screen.getByRole('link')).not.toHaveTextContent(/\d/)
  })

  it('shows no badge before the first answer — not a reassuring zero', () => {
    renderBell(unread(5))

    // Synchronously after render nothing has been answered.
    expect(screen.getByRole('link', { name: 'Notifications' })).not.toHaveTextContent(/\d/)
  })

  it('shows no badge, and no error, when the service cannot be reached', async () => {
    renderBell(apiResponse(502, { title: 'Bad Gateway' }))

    expect(screen.getByRole('link', { name: 'Notifications' })).not.toHaveTextContent(/\d/)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('caps the badge at 99+', async () => {
    renderBell(unread(1284))

    const link = await screen.findByRole('link', { name: 'Notifications, 1284 unread' })

    expect(link).toHaveTextContent('99+')
    expect(link).not.toHaveTextContent('1284')
  })

  it('shows 99 itself as 99', async () => {
    renderBell(unread(99))

    expect(await screen.findByRole('link', { name: 'Notifications, 99 unread' })).toHaveTextContent('99')
    expect(screen.getByRole('link')).not.toHaveTextContent('99+')
  })
})
