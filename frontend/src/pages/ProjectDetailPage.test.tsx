import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { ProjectDetailPage } from '@/pages/ProjectDetailPage'
import { apiResponse, stubFetch, type RecordedRequest } from '@/test/fake-fetch'
import type { Role } from '@/lib/roles'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'
const PROJECT_ID = 'b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b'
const CLIENT_ID = '6f9619ff-8b86-d011-b42d-00cf4fc964ff'
const ARCHITECT_ID = '11111111-1111-4111-8111-111111111111'
const PROJECT_MANAGER_ID = '22222222-2222-4222-8222-222222222222'

/**
 * The project as the service returns it: mid-lifecycle at Designing, staffed,
 * and with the two history entries that got it there.
 *
 * The counts are deliberately all different, so a test asserting one of them is
 * asserting that one and not accidentally matching another.
 */
function projectDetail(overrides: Record<string, unknown> = {}) {
  return {
    id: PROJECT_ID,
    clientId: CLIENT_ID,
    name: 'Beachfront villa',
    location: 'Galle',
    landSizePerches: 25.5,
    budget: 18500000,
    floors: 3,
    bedrooms: 4,
    bathrooms: 5,
    garageSpaces: 2,
    otherRequirements: 'Solar hot water',
    status: 'Designing',
    assignedArchitectId: ARCHITECT_ID,
    assignedProjectManagerId: PROJECT_MANAGER_ID,
    createdAt: '2026-08-01T09:00:00',
    updatedAt: '2026-08-04T09:00:00',
    statusHistory: [
      {
        id: 'aaaaaaaa-0000-4000-8000-000000000001',
        fromStatus: null,
        toStatus: 'Pending',
        changedByUserId: CLIENT_ID,
        changedByRole: 'Client',
        changedAt: '2026-08-01T09:00:00',
      },
      {
        id: 'aaaaaaaa-0000-4000-8000-000000000002',
        fromStatus: 'Pending',
        toStatus: 'Designing',
        changedByUserId: PROJECT_MANAGER_ID,
        changedByRole: 'ProjectManager',
        changedAt: '2026-08-04T09:00:00',
      },
    ],
    allowedNextStatuses: ['DesignApproved'],
    ...overrides,
  }
}

/** The same project once it has been moved on, as the PATCH reply carries it. */
function movedToDesignApproved() {
  const project = projectDetail()

  return {
    ...project,
    status: 'DesignApproved',
    statusHistory: [
      ...project.statusHistory,
      {
        id: 'aaaaaaaa-0000-4000-8000-000000000003',
        fromStatus: 'Designing',
        toStatus: 'DesignApproved',
        changedByUserId: ARCHITECT_ID,
        changedByRole: 'Architect',
        changedAt: '2026-08-10T09:00:00',
      },
    ],
    allowedNextStatuses: ['Construction'],
  }
}

/**
 * Stores a token shaped like the one the User Service issues, minus a real
 * signature — the app never verifies one and cannot, so this takes the same
 * path through `decodeToken` as a real token would.
 */
function signInAs(role: Role, userId: string) {
  const claims = {
    sub: userId,
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

function renderPage(
  who: { role: Role; userId: string },
  ...responses: Array<Response | Error>
): RecordedRequest[] {
  signInAs(who.role, who.userId)
  const requests = stubFetch(...responses)

  render(
    <MemoryRouter initialEntries={[`/projects/${PROJECT_ID}`]}>
      <AuthProvider>
        <Routes>
          <Route path="/projects/:projectId" element={<ProjectDetailPage />} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

const asOwningClient = { role: 'Client' as Role, userId: CLIENT_ID }
const asAssignedArchitect = { role: 'Architect' as Role, userId: ARCHITECT_ID }

/** The block holding one labelled fact about the project, found by its label. */
async function field(label: string): Promise<HTMLElement> {
  return (await screen.findByText(label)).parentElement as HTMLElement
}

/** The history rows, without the header row above them. */
function historyRows() {
  return screen.getAllByRole('row').slice(1)
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('ProjectDetailPage', () => {
  it('asks the service for the project named in the route', async () => {
    const requests = renderPage(asOwningClient, apiResponse(200, projectDetail()))

    await screen.findByText('Beachfront villa')
    expect(requests).toHaveLength(1)
    expect(requests[0].path).toBe(`/api/projects/${PROJECT_ID}`)
  })

  it('shows the requirements, the status and when the project was submitted', async () => {
    // The first AC bullet, field by field.
    renderPage(asOwningClient, apiResponse(200, projectDetail()))

    expect(await screen.findByText('Beachfront villa')).toBeInTheDocument()
    expect(screen.getByText('Designing')).toBeInTheDocument()
    expect(screen.getByText('25.5 perches')).toBeInTheDocument()
    expect(screen.getByText(/LKR/)).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
    expect(screen.getByText('4')).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument()
    expect(screen.getByText('Solar hot water')).toBeInTheDocument()
    // The formatted date is locale-dependent, so only the year is asserted —
    // testing Intl's output would be testing the browser, not the page.
    expect(screen.getByText(/Galle · submitted .*2026/)).toBeInTheDocument()
  })

  it('shows who is assigned to the project', async () => {
    renderPage(asOwningClient, apiResponse(200, projectDetail()))

    // Scoped to the field, because the same ids appear again further down as
    // the author of a history entry.
    expect(within(await field('Architect')).getByText(ARCHITECT_ID)).toBeInTheDocument()
    expect(within(await field('Project manager')).getByText(PROJECT_MANAGER_ID)).toBeInTheDocument()
  })

  it('says so plainly when nobody has been put on the project', async () => {
    // Which is every project so far — nothing assigns staff yet — so an empty
    // field has to read as "not yet" rather than as a page that failed to load.
    renderPage(
      asOwningClient,
      apiResponse(200, projectDetail({ assignedArchitectId: null, assignedProjectManagerId: null })),
    )

    await screen.findByText('Beachfront villa')
    expect(screen.getAllByText('Not yet assigned')).toHaveLength(2)
  })

  it('shows the full status history in chronological order', async () => {
    // The first AC bullet again: oldest first, and starting at the creation —
    // a history that begins halfway through is not the full record.
    renderPage(asOwningClient, apiResponse(200, projectDetail()))

    await screen.findByText('Beachfront villa')

    const rows = historyRows()
    expect(rows).toHaveLength(2)
    expect(within(rows[0]).getByText('Created as Pending')).toBeInTheDocument()
    expect(within(rows[0]).getByText('Client')).toBeInTheDocument()
    expect(within(rows[1]).getByText('Pending → Designing')).toBeInTheDocument()
    expect(within(rows[1]).getByText('Project Manager')).toBeInTheDocument()
  })

  it('names who made each change, so the record is auditable', async () => {
    renderPage(asOwningClient, apiResponse(200, projectDetail()))

    await screen.findByText('Beachfront villa')

    const rows = historyRows()
    expect(within(rows[0]).getByText(CLIENT_ID)).toBeInTheDocument()
    expect(within(rows[1]).getByText(PROJECT_MANAGER_ID)).toBeInTheDocument()
  })

  it('offers the assigned architect the one status the project may move to', async () => {
    // Driven by the service's allowedNextStatuses, not by a copy of the
    // transition table on this side.
    renderPage(asAssignedArchitect, apiResponse(200, projectDetail()))

    expect(
      await screen.findByRole('button', { name: 'Move to Design Approved' }),
    ).toBeInTheDocument()
  })

  it('offers the owning client no way to change the status', async () => {
    // They can see every step of their project, but declaring the design
    // approved is the company's word, not the customer's. The service refuses
    // them regardless; this only avoids inviting them into a 403.
    renderPage(asOwningClient, apiResponse(200, projectDetail()))

    await screen.findByText('Beachfront villa')
    expect(screen.queryByRole('button', { name: /Move to/ })).not.toBeInTheDocument()
  })

  it('tells staff when a project has nowhere left to go', async () => {
    renderPage(
      asAssignedArchitect,
      apiResponse(200, projectDetail({ status: 'Completed', allowedNextStatuses: [] })),
    )

    expect(
      await screen.findByText('This project is Completed and cannot move any further.'),
    ).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /Move to/ })).not.toBeInTheDocument()
  })

  it('asks the service to move the project and re-renders from its reply', async () => {
    // The third AC bullet from this side. The reply carries the project as it
    // now stands, history included, so nothing is refetched or guessed at.
    const requests = renderPage(
      asAssignedArchitect,
      apiResponse(200, projectDetail()),
      apiResponse(200, movedToDesignApproved()),
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Move to Design Approved' }))

    await waitFor(() => expect(requests).toHaveLength(2))
    expect(requests[1].path).toBe(`/api/projects/${PROJECT_ID}/status`)
    expect(requests[1].method).toBe('PATCH')
    expect(requests[1].body).toEqual({ status: 'DesignApproved' })

    expect(await screen.findByText('Design Approved')).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Move to Construction' })).toBeInTheDocument()
  })

  it('adds the change to the history it shows', async () => {
    renderPage(
      asAssignedArchitect,
      apiResponse(200, projectDetail()),
      apiResponse(200, movedToDesignApproved()),
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Move to Design Approved' }))

    await waitFor(() => expect(historyRows()).toHaveLength(3))
    expect(within(historyRows()[2]).getByText('Designing → Design Approved')).toBeInTheDocument()
  })

  it('does not send a status the project cannot be moved to', async () => {
    // There is no control for one: the only button offered is the move the
    // service said was available.
    renderPage(asAssignedArchitect, apiResponse(200, projectDetail()))

    await screen.findByText('Beachfront villa')
    expect(screen.queryByRole('button', { name: 'Move to Completed' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Move to Pending' })).not.toBeInTheDocument()
  })

  it('shows the reason the service gave when a move is refused', async () => {
    // A 400 here is a real answer that says where the project actually is.
    // Flattening it into "something went wrong" would throw that away.
    renderPage(
      asAssignedArchitect,
      apiResponse(200, projectDetail()),
      apiResponse(400, {
        title: 'Not a valid status change',
        detail: 'A Designing project cannot move to Completed. It can only move to DesignApproved.',
      }),
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Move to Design Approved' }))

    expect(
      await screen.findByText(
        'A Designing project cannot move to Completed. It can only move to DesignApproved.',
      ),
    ).toBeInTheDocument()
    // The project is still shown at the status it was actually left at.
    expect(screen.getByText('Designing')).toBeInTheDocument()
  })

  it('says so when somebody else moved the project first', async () => {
    renderPage(
      asAssignedArchitect,
      apiResponse(200, projectDetail()),
      apiResponse(409, {
        title: 'The project has moved on',
        detail: 'Somebody else changed this project’s status while you were looking at it.',
      }),
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Move to Design Approved' }))

    expect(
      await screen.findByText(/Somebody else changed this project/),
    ).toBeInTheDocument()
  })

  it('shows the service’s reason when the project is not the caller’s to open', async () => {
    // The second AC bullet from the browser's side. Being signed in as an
    // Architect is not the same as being this project's Architect.
    renderPage(
      { role: 'Architect', userId: '33333333-3333-4333-8333-333333333333' },
      apiResponse(403, {
        title: 'Not your project',
        detail:
          'Only the client who submitted this project, the staff assigned to it, or an administrator can view it.',
      }),
    )

    expect(
      await screen.findByText(
        'Only the client who submitted this project, the staff assigned to it, or an administrator can view it.',
      ),
    ).toBeInTheDocument()
    expect(screen.queryByText('Beachfront villa')).not.toBeInTheDocument()
  })

  it('falls back to a plain message when the request fails with no reason', async () => {
    renderPage(asOwningClient, new Error('offline'))

    expect(
      await screen.findByText('Could not load this project. Please try again.'),
    ).toBeInTheDocument()
  })
})
