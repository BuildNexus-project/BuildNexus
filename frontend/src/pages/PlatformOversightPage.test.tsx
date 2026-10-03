import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import App from '@/App'
import { AuthProvider } from '@/auth/AuthProvider'
import type { OversightProject, ProjectOversight } from '@/lib/oversight-api'
import type { Role } from '@/lib/roles'
import { PlatformOversightPage } from '@/pages/PlatformOversightPage'
import { apiResponse, stubRoutes } from '@/test/fake-fetch'
import { signInAs } from '@/test/sign-in'

/**
 * Platform oversight (US-38): every project with its status, staff and last-updated date,
 * stalled projects highlighted, and a link to each report.
 */
const OVERSIGHT_PATH = '/api/projects/oversight'

const VILLA = '11111111-1111-4111-8111-111111111111'
const COTTAGE = '22222222-2222-4222-8222-222222222222'
const TOWER = '33333333-3333-4333-8333-333333333333'

const DAY_MS = 24 * 60 * 60 * 1000

/** An ISO timestamp this many days (and an hour, so the day count is not on a knife-edge) ago. */
const daysAgo = (days: number) => new Date(Date.now() - days * DAY_MS - 60 * 60 * 1000).toISOString()

function project(overrides: Partial<OversightProject> & Pick<OversightProject, 'id' | 'name'>): OversightProject {
  return {
    location: 'Galle',
    status: 'Designing',
    assignedArchitectId: null,
    assignedArchitectName: null,
    assignedProjectManagerId: null,
    assignedProjectManagerName: null,
    createdAt: daysAgo(60),
    updatedAt: daysAgo(1),
    isStalled: false,
    ...overrides,
  }
}

function oversightOf(projects: OversightProject[]): ProjectOversight {
  return {
    generatedAt: new Date().toISOString(),
    stalledAfterDays: 14,
    totalProjects: projects.length,
    stalledCount: projects.filter((p) => p.isStalled).length,
    projects,
  }
}

const PLATFORM = oversightOf([
  project({
    id: VILLA,
    name: 'Beachfront villa',
    status: 'Construction',
    assignedArchitectId: 'a1',
    assignedArchitectName: 'Amara Perera',
    assignedProjectManagerId: 'm1',
    assignedProjectManagerName: 'Nuwan Silva',
    updatedAt: '2026-09-28T09:00:00Z',
  }),
  project({
    id: COTTAGE,
    name: 'Hilltop cottage',
    status: 'Pending',
    updatedAt: daysAgo(30),
    isStalled: true,
  }),
  project({
    id: TOWER,
    name: 'City tower',
    status: 'Cancelled',
    assignedArchitectId: 'a2',
  }),
])

function renderPage(routes: Record<string, Response | Error>) {
  signInAs('Admin')
  const requests = stubRoutes(routes)

  render(
    <MemoryRouter>
      <AuthProvider>
        <PlatformOversightPage />
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

const rowOf = (id: string) => screen.getByTestId(`project-${id}`)

afterEach(() => {
  vi.unstubAllGlobals()
  localStorage.clear()
})

describe('PlatformOversightPage — system-wide project list (AC-1)', () => {
  it('asks the service for every project when it opens', async () => {
    const requests = renderPage({ [OVERSIGHT_PATH]: apiResponse(200, PLATFORM) })

    await screen.findByText('Beachfront villa')

    expect(requests.map((request) => request.path)).toEqual([OVERSIGHT_PATH])
  })

  it('lists every project, cancelled ones included', async () => {
    renderPage({ [OVERSIGHT_PATH]: apiResponse(200, PLATFORM) })

    expect(await screen.findByText('Beachfront villa')).toBeInTheDocument()
    expect(screen.getByText('Hilltop cottage')).toBeInTheDocument()
    expect(screen.getByText('City tower')).toBeInTheDocument()
    expect(within(rowOf(TOWER)).getByText('Cancelled')).toBeInTheDocument()
    expect(screen.getByTestId('oversight-summary')).toHaveTextContent('3 projects')
  })

  it('shows each project’s status, assigned staff and last-updated date', async () => {
    renderPage({ [OVERSIGHT_PATH]: apiResponse(200, PLATFORM) })
    await screen.findByText('Beachfront villa')

    const row = within(rowOf(VILLA))

    expect(row.getByText('Construction')).toBeInTheDocument()
    expect(row.getByText('Amara Perera')).toBeInTheDocument()
    expect(row.getByText('Nuwan Silva')).toBeInTheDocument()
    expect(row.getByText(/2026/)).toBeInTheDocument()
  })

  it('links each project to its own page', async () => {
    renderPage({ [OVERSIGHT_PATH]: apiResponse(200, PLATFORM) })

    expect(await screen.findByRole('link', { name: 'Beachfront villa' })).toHaveAttribute(
      'href',
      `/projects/${VILLA}`,
    )
  })

  it('says so when a project has nobody on it, and when a name is unknown', async () => {
    renderPage({ [OVERSIGHT_PATH]: apiResponse(200, PLATFORM) })
    await screen.findByText('Hilltop cottage')

    expect(within(rowOf(COTTAGE)).getAllByText('Not assigned')).toHaveLength(2)
    // City tower has an Architect the User Service could not name: filled, not empty.
    expect(within(rowOf(TOWER)).getByText('Assigned (name unavailable)')).toBeInTheDocument()
  })

  it('says there are no projects yet when the platform is empty', async () => {
    renderPage({ [OVERSIGHT_PATH]: apiResponse(200, oversightOf([])) })

    expect(await screen.findByText('There are no projects yet.')).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })

  it('says it is loading before the list arrives', () => {
    renderPage({ [OVERSIGHT_PATH]: apiResponse(200, PLATFORM) })

    expect(screen.getByRole('status')).toHaveTextContent('Loading the projects…')
  })

  it('shows an alert, and no list, when the projects cannot be loaded', async () => {
    renderPage({ [OVERSIGHT_PATH]: apiResponse(500, { title: 'Server error' }) })

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
  })
})

describe('PlatformOversightPage — stalled projects (AC-2)', () => {
  it('highlights a stalled project and says so in words, not just colour', async () => {
    renderPage({ [OVERSIGHT_PATH]: apiResponse(200, PLATFORM) })
    await screen.findByText('Hilltop cottage')

    expect(rowOf(COTTAGE)).toHaveAttribute('data-stalled', 'true')
    expect(within(rowOf(COTTAGE)).getByText('Stalled · 30 days')).toBeInTheDocument()
  })

  it('does not highlight a project that is moving, or one that is finished', async () => {
    renderPage({ [OVERSIGHT_PATH]: apiResponse(200, PLATFORM) })
    await screen.findByText('Hilltop cottage')

    expect(rowOf(VILLA)).not.toHaveAttribute('data-stalled')
    expect(rowOf(TOWER)).not.toHaveAttribute('data-stalled')
    expect(within(rowOf(VILLA)).queryByText(/Stalled/)).not.toBeInTheDocument()
  })

  it('counts the stalled projects and quotes the service’s threshold', async () => {
    renderPage({ [OVERSIGHT_PATH]: apiResponse(200, PLATFORM) })

    await screen.findByText('Hilltop cottage')

    expect(screen.getByTestId('oversight-summary')).toHaveTextContent(
      '1 stalled (open, with no update for 14 days or more)',
    )
  })

  it('narrows the list to the stalled projects on request, and back', async () => {
    const user = userEvent.setup()
    renderPage({ [OVERSIGHT_PATH]: apiResponse(200, PLATFORM) })
    await screen.findByText('Hilltop cottage')

    await user.click(screen.getByRole('checkbox', { name: 'Show only stalled projects' }))

    expect(screen.getByText('Hilltop cottage')).toBeInTheDocument()
    expect(screen.queryByText('Beachfront villa')).not.toBeInTheDocument()

    await user.click(screen.getByRole('checkbox', { name: 'Show only stalled projects' }))

    expect(screen.getByText('Beachfront villa')).toBeInTheDocument()
  })

  it('says so when nothing is stalled and the list is narrowed', async () => {
    const user = userEvent.setup()
    renderPage({ [OVERSIGHT_PATH]: apiResponse(200, oversightOf([project({ id: VILLA, name: 'Busy' })])) })
    await screen.findByText('Busy')

    await user.click(screen.getByRole('checkbox', { name: 'Show only stalled projects' }))

    expect(screen.getByText('No projects are stalled.')).toBeInTheDocument()
  })
})

describe('PlatformOversightPage — the reporting suite (AC-3)', () => {
  it('links to the project, construction & payment, and design approval reports', async () => {
    renderPage({ [OVERSIGHT_PATH]: apiResponse(200, PLATFORM) })

    const reports = within(
      screen.getByRole('heading', { name: 'Reports' }).closest('[data-slot="card"]') as HTMLElement,
    )

    expect(reports.getByRole('link', { name: /Project report/ })).toHaveAttribute(
      'href',
      '/admin/reports/project-status',
    )
    expect(reports.getByRole('link', { name: /Build & payment/ })).toHaveAttribute(
      'href',
      '/reports/construction-payment',
    )
    expect(reports.getByRole('link', { name: /Design report/ })).toHaveAttribute(
      'href',
      '/admin/reports/design-approval',
    )
    expect(reports.getAllByRole('link')).toHaveLength(3)

    await screen.findByText('Beachfront villa')
  })

  it('offers the reports even when the project list cannot be loaded', async () => {
    renderPage({ [OVERSIGHT_PATH]: apiResponse(500, { title: 'Server error' }) })

    await screen.findByRole('alert')

    expect(screen.getByRole('link', { name: /Design report/ })).toBeInTheDocument()
  })
})

describe('the oversight route — Admin only', () => {
  function openRoute(role: Role) {
    signInAs(role)
    window.history.pushState({}, '', '/admin/oversight')

    render(
      <AuthProvider>
        <App />
      </AuthProvider>,
    )
  }

  afterEach(() => {
    window.history.pushState({}, '', '/')
  })

  it('opens for an Admin', async () => {
    stubRoutes({ [OVERSIGHT_PATH]: apiResponse(200, PLATFORM) })

    openRoute('Admin')

    expect(await screen.findByText('All projects')).toBeInTheDocument()
    await waitFor(() => expect(screen.getByText('Beachfront villa')).toBeInTheDocument())
  })

  it.each<Role>(['Client', 'Architect', 'ProjectManager'])(
    'tells a %s they do not have access, and never asks for the list',
    (role) => {
      const requests = stubRoutes({ [OVERSIGHT_PATH]: apiResponse(200, PLATFORM) })

      openRoute(role)

      expect(screen.getByText('You do not have access to this page')).toBeInTheDocument()
      expect(screen.queryByText('All projects')).not.toBeInTheDocument()
      expect(requests.filter((request) => request.path === OVERSIGHT_PATH)).toHaveLength(0)
    },
  )
})
