import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { HomePage } from '@/pages/HomePage'
import { apiResponse, stubRoutes, type RecordedRequest } from '@/test/fake-fetch'
import {
  ARCHITECT_PATHS,
  CLIENT_PATHS,
  architectRoutes,
  clientRoutes,
} from '@/test/dashboard-fixtures'
import { TEST_USER_ID, signInAs } from '@/test/sign-in'
import type { Role } from '@/lib/roles'

function project(status: string, index: number) {
  return {
    id: `b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a${String(index).padStart(2, '0')}`,
    clientId: TEST_USER_ID,
    name: `Project ${index}`,
    location: 'Galle',
    status,
    createdAt: '2026-08-01T09:00:00',
    updatedAt: '2026-08-04T09:00:00',
  }
}

/** Two still in design, one being built, one finished. */
function projects() {
  return [
    project('Pending', 1),
    project('DesignApproved', 2),
    project('Construction', 3),
    project('Completed', 4),
  ]
}

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
    '/api/projects': apiResponse(200, projects()),
    ...clientRoutes(),
    ...architectRoutes(),
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

describe('HomePage role dashboards', () => {
  it('shows a Client their own dashboard, from the four services that hold their data', async () => {
    const requests = renderPage('Client')

    expect(await screen.findByRole('heading', { name: 'Your active projects' })).toBeInTheDocument()
    await screen.findByRole('link', { name: 'Beachfront villa' })

    expect(requests.map((request) => request.path).sort()).toEqual(Object.values(CLIENT_PATHS).sort())
  })

  it('shows an Architect their own dashboard, from the two services that hold their data', async () => {
    const requests = renderPage('Architect')

    expect(await screen.findByRole('heading', { name: 'Revisions to make' })).toBeInTheDocument()
    await screen.findByRole('link', { name: 'Beachfront villa' })

    expect(requests.map((request) => request.path).sort()).toEqual(Object.values(ARCHITECT_PATHS).sort())
  })

  it('does not ask for a Client’s dashboard on behalf of anyone else', async () => {
    // Each service refuses the wrong role anyway; this pins that the page does not even
    // try, which would otherwise be a 403 in the console on every visit.
    const requests = renderPage('ProjectManager')

    await screen.findByText('Your assigned projects at a glance')

    const paths = requests.map((request) => request.path)
    for (const path of [...Object.values(CLIENT_PATHS), ...Object.values(ARCHITECT_PATHS)]) {
      expect(paths).not.toContain(path)
    }
  })

  it('does not show a Client the project summary of the other roles as well', async () => {
    const requests = renderPage('Client')

    await screen.findByRole('link', { name: 'Beachfront villa' })

    // The old summary read the plain project list; a Client's dashboard has its own.
    expect(requests.map((request) => request.path)).not.toContain('/api/projects')
    expect(screen.queryByText('Under construction')).not.toBeInTheDocument()
  })
})

describe('HomePage project summary', () => {
  it('asks the service for the projects the caller may see', async () => {
    const requests = renderPage('ProjectManager')

    await screen.findByText('Your assigned projects at a glance')
    expect(requests).toHaveLength(1)
    expect(requests[0].path).toBe('/api/projects')
  })

  it('counts where the projects stand', async () => {
    renderPage('ProjectManager')

    await screen.findByText('Your assigned projects at a glance')

    const tile = (label: string) => screen.getByText(label).closest('a') as HTMLElement
    expect(tile('Active projects')).toHaveTextContent('4')
    // Pending and Design Approved are both still design.
    expect(tile('In design')).toHaveTextContent('2')
    expect(tile('Under construction')).toHaveTextContent('1')
    expect(tile('Completed')).toHaveTextContent('1')
  })

  it('shows zeroes, not nothing, when there are no projects yet', async () => {
    renderPage('ProjectManager', { '/api/projects': apiResponse(200, []) })

    await screen.findByText('Your assigned projects at a glance')

    expect(screen.getByText('Active projects').closest('a')).toHaveTextContent('0')
  })

  it.each<[Role, string]>([
    ['ProjectManager', 'Your assigned projects at a glance'],
    ['Admin', 'Every project at a glance'],
  ])('heads it for a %s as “%s”', async (role, heading) => {
    renderPage(role)

    expect(await screen.findByText(heading)).toBeInTheDocument()
  })

  it('leaves the summary out, rather than showing it wrong, when the projects cannot be read', async () => {
    renderPage('ProjectManager', { '/api/projects': apiResponse(500, { title: 'Boom' }) })

    // The rest of the page is unaffected: the summary is a convenience, so a
    // failed read must neither show an error nor leave a page of dashes.
    expect(await screen.findByText('Where would you like to go?')).toBeInTheDocument()
    expect(screen.queryByText('Your assigned projects at a glance')).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('does not show a summary before the projects have arrived', () => {
    renderPage('ProjectManager')

    // Synchronously after render, the request has not been answered.
    expect(screen.queryByText('Your assigned projects at a glance')).not.toBeInTheDocument()
  })
})
