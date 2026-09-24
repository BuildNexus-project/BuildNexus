import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { ConstructionProgressPage } from '@/pages/ConstructionProgressPage'
import { apiResponse, stubFetch, type RecordedRequest } from '@/test/fake-fetch'
import type { Role } from '@/lib/roles'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'
const CLIENT_ID = '6f9619ff-8b86-d011-b42d-00cf4fc964ff'
const BUILDING_ID = 'b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b'
const PENDING_ID = 'c3e5a7b9-2d4f-4e6a-9b0c-1d2e3f4a5b6c'
/**
 * A fixed milestone id, for the tests that read the same milestone across two
 * refreshes. `milestone()` defaults its id to `crypto.randomUUID()`, so the
 * parameter is inferred as a UUID-shaped template literal — a placeholder like
 * `'fixed-id'` does not type-check against it.
 */
const MILESTONE_ID = 'd4f6a8b0-3e5f-4a7b-9c0d-1e2f3a4b5c6d'

/** One project that has reached construction, and one that has not. */
function projects() {
  return [
    {
      id: BUILDING_ID,
      clientId: CLIENT_ID,
      name: 'Beachfront villa',
      location: 'Galle',
      status: 'Construction',
      createdAt: '2026-08-01T09:00:00',
      updatedAt: '2026-08-04T09:00:00',
    },
    {
      id: PENDING_ID,
      clientId: CLIENT_ID,
      name: 'City townhouse',
      location: 'Colombo',
      status: 'Designing',
      createdAt: '2026-07-20T09:00:00',
      updatedAt: '2026-08-02T09:00:00',
    },
  ]
}

function milestone(name: string, status: string, id = crypto.randomUUID()) {
  return {
    id,
    projectId: BUILDING_ID,
    name,
    status,
    createdAtUtc: '2026-08-01T09:00:00Z',
    updatedAtUtc: '2026-08-04T09:00:00Z',
  }
}

function summary(completed: number, percent: number, milestones: ReturnType<typeof milestone>[]) {
  return {
    projectId: BUILDING_ID,
    totalMilestones: milestones.length,
    completedMilestones: completed,
    progressPercent: percent,
    milestones,
  }
}

/**
 * Stores a token shaped like the one the User Service issues, minus a real
 * signature — the app never verifies one and cannot.
 */
function signInAs(role: Role) {
  const claims = {
    sub: CLIENT_ID,
    name: 'Ada Perera',
    email: 'ada@example.com',
    role,
    exp: Math.floor(Date.now() / 1000) + 3600,
  }

  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')

  localStorage.setItem(TOKEN_STORAGE_KEY, `header.${payload}.signature`)
}

function renderPage(...responses: Array<Response | Error>): RecordedRequest[] {
  signInAs('Client')
  const requests = stubFetch(...responses)

  render(
    <MemoryRouter>
      <AuthProvider>
        <ConstructionProgressPage />
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('ConstructionProgressPage', () => {
  it('shows each milestone with its status and the overall percentage', async () => {
    // US-13 AC-1: per-milestone status, and an overall percentage and bar.
    renderPage(
      apiResponse(200, projects()),
      apiResponse(
        200,
        summary(1, 33.33, [
          milestone('Foundation', 'Completed'),
          milestone('Walls', 'InProgress'),
          milestone('Roof', 'NotStarted'),
        ]),
      ),
    )

    const card = await screen.findByTestId(`progress-${BUILDING_ID}`)

    expect(within(card).getByText('33.33%')).toBeInTheDocument()
    expect(within(card).getByText('1 of 3 milestones complete')).toBeInTheDocument()

    expect(within(card).getByText('Foundation')).toBeInTheDocument()
    expect(within(card).getByText('Completed')).toBeInTheDocument()
    expect(within(card).getByText('In Progress')).toBeInTheDocument()
    expect(within(card).getByText('Not Started')).toBeInTheDocument()

    const bar = within(card).getByRole('progressbar')
    expect(bar).toHaveAttribute('aria-valuenow', '33')
  })

  it('only asks for progress on projects that have reached construction', async () => {
    // A Pending or Designing project has no milestone_setups row, so asking
    // would earn a 404 that only means "not yet".
    const requests = renderPage(
      apiResponse(200, projects()),
      apiResponse(200, summary(0, 0, [])),
    )

    await screen.findByTestId(`progress-${BUILDING_ID}`)

    expect(requests.map((request) => request.path)).toEqual([
      '/api/projects',
      `/api/construction/projects/${BUILDING_ID}/progress-summary`,
    ])
    expect(screen.queryByTestId(`progress-${PENDING_ID}`)).not.toBeInTheDocument()
  })

  it('says construction is not planned yet when the service has no plan', async () => {
    // A 404 is a real state — the design was approved but the DesignApproved
    // event has not been consumed yet — not a failure to show in red.
    renderPage(
      apiResponse(200, projects()),
      apiResponse(404, { title: 'Progress not available.' }),
    )

    const card = await screen.findByTestId(`progress-${BUILDING_ID}`)

    expect(within(card).getByText(/Construction has not been planned yet/)).toBeInTheDocument()
    expect(within(card).queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows an approved project with no milestones as zero percent', async () => {
    renderPage(apiResponse(200, projects()), apiResponse(200, summary(0, 0, [])))

    const card = await screen.findByTestId(`progress-${BUILDING_ID}`)

    expect(within(card).getByText('0.00%')).toBeInTheDocument()
    expect(
      within(card).getByText(/has not set out the milestones yet/),
    ).toBeInTheDocument()
  })

  it('contains a failing project to its own card', async () => {
    // One project the service is unhappy about must not blank the dashboard.
    renderPage(
      apiResponse(200, projects()),
      apiResponse(403, { detail: 'You can only view construction progress for your own projects.' }),
    )

    const card = await screen.findByTestId(`progress-${BUILDING_ID}`)

    expect(within(card).getByRole('alert')).toHaveTextContent(
      'You can only view construction progress for your own projects.',
    )
    // The card itself still renders, with the project's name on it.
    expect(within(card).getByText('Beachfront villa')).toBeInTheDocument()
  })

  it('reports a failure to load the project list in the service own words', async () => {
    renderPage(apiResponse(500, { title: 'Something went wrong.' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Something went wrong.')
  })

  it('falls back to its own wording when the failure carries no message', async () => {
    // A network error rather than a refused request — there is no problem
    // -details body to quote, so the page has to say something itself.
    renderPage(new TypeError('Failed to fetch'))

    expect(await screen.findByRole('alert')).toHaveTextContent(/Could not load your projects/)
  })

  it('says so when no project has reached construction', async () => {
    renderPage(apiResponse(200, [projects()[1]]))

    expect(
      await screen.findByText(/None of your projects have reached construction yet/),
    ).toBeInTheDocument()
  })

  it('picks up a status change without the Client doing anything', async () => {
    // US-13 AC-2: the dashboard reflects the latest data with no manual refresh
    // workflow. Here the Client returns to the tab and the new status is shown
    // — no button was pressed and nothing was remounted.
    renderPage(
      apiResponse(200, projects()),
      apiResponse(200, summary(0, 0, [milestone('Foundation', 'NotStarted', MILESTONE_ID)])),
      apiResponse(200, projects()),
      apiResponse(200, summary(1, 100, [milestone('Foundation', 'Completed', MILESTONE_ID)])),
    )

    const card = await screen.findByTestId(`progress-${BUILDING_ID}`)
    expect(within(card).getByText('Not Started')).toBeInTheDocument()
    expect(within(card).getByText('0.00%')).toBeInTheDocument()

    // The tab goes away and comes back, which is what the page listens for.
    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
    document.dispatchEvent(new Event('visibilitychange'))

    await waitFor(() => {
      expect(within(card).getByText('Completed')).toBeInTheDocument()
    })
    expect(within(card).getByText('100.00%')).toBeInTheDocument()
    expect(within(card).queryByText('Not Started')).not.toBeInTheDocument()
  })

  it('keeps the data on screen when a later refresh fails', async () => {
    // Blanking a dashboard the Client was reading because one poll did not land
    // would be worse than showing what was last known.
    renderPage(
      apiResponse(200, projects()),
      apiResponse(200, summary(1, 100, [milestone('Foundation', 'Completed', MILESTONE_ID)])),
      apiResponse(503, { title: 'Service unavailable.' }),
    )

    const card = await screen.findByTestId(`progress-${BUILDING_ID}`)
    expect(within(card).getByText('100.00%')).toBeInTheDocument()

    vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('visible')
    document.dispatchEvent(new Event('visibilitychange'))

    await screen.findByRole('alert')
    // The milestone and its percentage are still there beside the error.
    expect(within(card).getByText('100.00%')).toBeInTheDocument()
    expect(within(card).getByText('Foundation')).toBeInTheDocument()
  })
})
