import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { HomePage } from '@/pages/HomePage'
import { apiResponse, stubRoutes, type RecordedRequest } from '@/test/fake-fetch'
import {
  ADMIN_PATHS,
  ARCHITECT_PATHS,
  CLIENT_PATHS,
  NOTIFICATIONS_PATH,
  PM_PATHS,
  adminRoutes,
  architectRoutes,
  clientRoutes,
  notificationRoutes,
  pmRoutes,
} from '@/test/dashboard-fixtures'
import { signInAs } from '@/test/sign-in'
import type { Role } from '@/lib/roles'

/**
 * Renders the page for a signed-in user of the given role, with every service a
 * dashboard might ask answering. What the page asks of them depends on the role, so
 * the routes for every role are stubbed and the requests recorded — which is how a
 * test tells what a role was, and was not, shown.
 */
function renderPage(
  role: Role,
  overrides: Record<string, Response | Error> = {},
  fullName?: string,
): RecordedRequest[] {
  signInAs(role, fullName)
  const requests = stubRoutes({
    ...notificationRoutes(),
    ...clientRoutes(),
    ...architectRoutes(),
    ...pmRoutes(),
    ...adminRoutes(),
    ...overrides,
  })

  render(
    <MemoryRouter>
      <AuthProvider>
        <HomePage />
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

/** The destination cards under "Where would you like to go?". */
function destinations(): string[] {
  const section = screen.getByRole('heading', { name: 'Where would you like to go?' })
    .parentElement as HTMLElement

  return within(section)
    .getAllByRole('link')
    .map((link) => within(link).getByRole('heading').textContent ?? '')
}

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe('HomePage', () => {
  it('welcomes the user by their first name', () => {
    renderPage('Client', {}, 'Ayesha Rahman')

    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Welcome back, Ayesha.')
  })

  it('names the role’s workspace', () => {
    renderPage('Architect')

    expect(screen.getByText('Architect workspace')).toBeInTheDocument()
  })

  it.each<[Role, string, string]>([
    ['Client', 'New project', '/projects/new'],
    ['Architect', 'Projects', '/projects'],
    ['ProjectManager', 'Projects', '/projects'],
    ['Admin', 'Users', '/admin/users'],
  ])('leads a %s to %s', (role, label, to) => {
    renderPage(role)

    // The button in the welcome banner, first among the links.
    const banner = screen.getByRole('heading', { level: 1 }).closest('section') as HTMLElement
    expect(within(banner).getByRole('link', { name: new RegExp(label) })).toHaveAttribute('href', to)
  })

  it.each<[Role, string[]]>([
    ['Client', ['Projects', 'New project', 'Progress', 'My costs', 'Your profile']],
    ['Architect', ['Projects', 'Project team', 'Your profile']],
    ['ProjectManager', ['Projects', 'Project team', 'Build & payment', 'Your profile']],
    ['Admin', ['Projects', 'Users', 'Project report', 'Design report', 'Build & payment', 'Your profile']],
  ])('offers a %s their own pages and the profile', (role, expected) => {
    renderPage(role)

    expect(destinations()).toEqual(expected)
  })
})

/** What each role's dashboard is made of: its heading, and every slice it asks a service for. */
const DASHBOARDS: [Role, string, string[]][] = [
  // The Client and the Architect also read their notifications (US-26); the other two are
  // never stored any, so they are not asked.
  ['Client', 'Your active projects', [...Object.values(CLIENT_PATHS), NOTIFICATIONS_PATH]],
  ['Architect', 'Revisions to make', [...Object.values(ARCHITECT_PATHS), NOTIFICATIONS_PATH]],
  // The Project Manager's names come from the project list, the one they may read.
  ['ProjectManager', 'Active construction', Object.values(PM_PATHS)],
  ['Admin', 'System-wide counts', Object.values(ADMIN_PATHS)],
]

describe('HomePage role dashboards', () => {
  it.each(DASHBOARDS)('shows a %s their own dashboard, headed “%s”', async (role, heading) => {
    renderPage(role)

    expect(await screen.findByRole('heading', { name: heading })).toBeInTheDocument()
  })

  it.each(DASHBOARDS)(
    'asks a %s only for the slices that role’s dashboard is made of',
    async (role, heading, expectedPaths) => {
      // Each service refuses the wrong role anyway; this pins that the page does not even
      // try, which would otherwise be a 403 in the console on every visit.
      const requests = renderPage(role)

      await screen.findByRole('heading', { name: heading })

      expect(requests.map((request) => request.path).sort()).toEqual([...expectedPaths].sort())
    },
  )

  it.each(DASHBOARDS)('shows a %s only their own dashboard, never another role’s', async (role, heading) => {
    renderPage(role)

    await screen.findByRole('heading', { name: heading })

    const others = DASHBOARDS.filter(([other]) => other !== role).map(([, otherHeading]) => otherHeading)
    for (const otherHeading of others) {
      expect(screen.queryByRole('heading', { name: otherHeading })).not.toBeInTheDocument()
    }
  })

  it('no longer shows the old four-tile project summary to anyone', async () => {
    renderPage('ProjectManager')

    await screen.findByRole('heading', { name: 'Active construction' })

    expect(screen.queryByText('Under construction')).not.toBeInTheDocument()
    expect(screen.queryByText('In design')).not.toBeInTheDocument()
  })

  it('leaves the rest of the page working when a dashboard’s services cannot answer', async () => {
    renderPage('Admin', {
      [ADMIN_PATHS.users]: apiResponse(500, { title: 'Boom' }),
      [ADMIN_PATHS.projects]: apiResponse(500, { title: 'Boom' }),
    })

    expect(await screen.findAllByRole('alert')).toHaveLength(2)
    // The welcome and the way to every page are unaffected by a dashboard that failed.
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Welcome back')
    expect(screen.getByText('Where would you like to go?')).toBeInTheDocument()
  })
})

describe('HomePage notifications (US-26)', () => {
  it.each(['Client', 'Architect'] as const)(
    'shows a %s what was stored for them as soon as the dashboard loads',
    async (role) => {
      renderPage(role)

      // Nothing was clicked: stored earlier, visible on this load.
      expect(await screen.findByRole('heading', { name: 'Notifications' })).toBeInTheDocument()
      expect(await screen.findByText('A payment was received on "Beachfront villa".')).toBeInTheDocument()
      expect(screen.getByText('2 new')).toBeInTheDocument()
    },
  )

  it.each(['ProjectManager', 'Admin'] as const)(
    'shows a %s no notifications panel, since nothing is stored for them',
    async (role) => {
      const requests = renderPage(role)

      await screen.findByRole('heading', { name: 'Where would you like to go?' })

      expect(screen.queryByRole('heading', { name: 'Notifications' })).not.toBeInTheDocument()
      expect(requests.map((request) => request.path)).not.toContain(NOTIFICATIONS_PATH)
    },
  )

  it('puts the notifications above the dashboard, where a returning user looks first', async () => {
    renderPage('Client')

    const notifications = await screen.findByRole('heading', { name: 'Notifications' })
    const projects = await screen.findByRole('heading', { name: 'Your projects at a glance' })

    expect(
      notifications.compareDocumentPosition(projects) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy()
  })

  it('leaves the dashboard working when the notifications cannot be loaded', async () => {
    renderPage('Client', { [NOTIFICATIONS_PATH]: apiResponse(502, { title: 'Bad Gateway' }) })

    expect(await screen.findByText('Your notifications could not be loaded right now.')).toBeInTheDocument()
    expect(await screen.findByRole('link', { name: 'Beachfront villa' })).toBeInTheDocument()
  })
})
