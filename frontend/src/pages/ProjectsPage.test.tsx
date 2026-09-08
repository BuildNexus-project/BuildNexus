import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { ProjectsPage } from '@/pages/ProjectsPage'
import { apiResponse, stubFetch, type RecordedRequest } from '@/test/fake-fetch'
import type { Role } from '@/lib/roles'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'
const CLIENT_ID = '6f9619ff-8b86-d011-b42d-00cf4fc964ff'
const FIRST_ID = 'b2d4f6a8-1c3e-4d5f-8a9b-0c1d2e3f4a5b'
const SECOND_ID = 'c3e5a7b9-2d4f-4e6a-9b0c-1d2e3f4a5b6c'

function projects() {
  return [
    {
      id: FIRST_ID,
      clientId: CLIENT_ID,
      name: 'Beachfront villa',
      location: 'Galle',
      status: 'Designing',
      createdAt: '2026-08-01T09:00:00',
      updatedAt: '2026-08-04T09:00:00',
    },
    {
      id: SECOND_ID,
      clientId: CLIENT_ID,
      name: 'City townhouse',
      location: 'Colombo',
      status: 'DesignApproved',
      createdAt: '2026-07-20T09:00:00',
      updatedAt: '2026-08-02T09:00:00',
    },
  ]
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

function renderPage(role: Role, ...responses: Array<Response | Error>): RecordedRequest[] {
  signInAs(role)
  const requests = stubFetch(...responses)

  render(
    <MemoryRouter>
      <AuthProvider>
        <ProjectsPage />
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('ProjectsPage', () => {
  it('asks the service for the projects the caller may see', async () => {
    // No filter is sent: the service scopes the list to the caller's own
    // involvement from their token, so asking for a subset here would be asking
    // for something we are not the ones to decide.
    const requests = renderPage('Client', apiResponse(200, projects()))

    await screen.findByText('Beachfront villa')
    expect(requests).toHaveLength(1)
    expect(requests[0].path).toBe('/api/projects')
  })

  it('lists each project with its location and status', async () => {
    renderPage('Client', apiResponse(200, projects()))

    const rows = (await screen.findAllByRole('row')).slice(1)

    expect(rows).toHaveLength(2)
    expect(within(rows[0]).getByText('Beachfront villa')).toBeInTheDocument()
    expect(within(rows[0]).getByText('Galle')).toBeInTheDocument()
    expect(within(rows[0]).getByText('Designing')).toBeInTheDocument()
    // The wire value is DesignApproved; nobody should have to read that.
    expect(within(rows[1]).getByText('Design Approved')).toBeInTheDocument()
  })

  it('links each row to that project', async () => {
    renderPage('Client', apiResponse(200, projects()))

    expect(await screen.findByRole('link', { name: 'Beachfront villa' })).toHaveAttribute(
      'href',
      `/projects/${FIRST_ID}`,
    )
    expect(screen.getByRole('link', { name: 'City townhouse' })).toHaveAttribute(
      'href',
      `/projects/${SECOND_ID}`,
    )
  })

  it('tells a client with no projects that they have not submitted one', async () => {
    renderPage('Client', apiResponse(200, []))

    expect(await screen.findByText('You have not submitted a project yet.')).toBeInTheDocument()
  })

  it('tells staff with no projects that they have not been assigned one', async () => {
    // Which is every member of staff so far — nothing assigns them yet — so
    // this is the message they actually get, and it should say why rather than
    // reading as an error.
    renderPage('Architect', apiResponse(200, []))

    expect(
      await screen.findByText('You have not been assigned to a project yet.'),
    ).toBeInTheDocument()
  })

  it('shows the reason the service gave when the list cannot be loaded', async () => {
    renderPage(
      'Client',
      apiResponse(401, {
        title: 'Not authenticated',
        detail: 'Your session has expired. Sign in again to continue.',
      }),
    )

    expect(
      await screen.findByText('Your session has expired. Sign in again to continue.'),
    ).toBeInTheDocument()
  })

  it('falls back to a plain message when the request fails with no reason', async () => {
    renderPage('Client', new Error('offline'))

    await waitFor(() =>
      expect(
        screen.getByText('Could not load your projects. Please try again.'),
      ).toBeInTheDocument(),
    )
  })

  // US-08: a cancelled project is not being worked on, so it is off the active
  // list. The service does the filtering — this side only asks it to.
  it('leaves cancelled projects out until the toggle is ticked', async () => {
    const active = projects()
    const withCancelled = [
      ...active,
      {
        id: 'd4f6a8b0-3e5a-4f6b-0c1d-2e3f4a5b6c7d',
        clientId: CLIENT_ID,
        name: 'Hillside cabin',
        location: 'Ella',
        status: 'Cancelled',
        createdAt: '2026-06-01T09:00:00',
        updatedAt: '2026-06-10T09:00:00',
      },
    ]

    const requests = renderPage(
      'Client',
      apiResponse(200, active),
      apiResponse(200, withCancelled),
    )

    await screen.findByText('Beachfront villa')
    expect(screen.queryByText('Hillside cabin')).not.toBeInTheDocument()
    // Nothing asked for cancelled projects, so the plain list is requested.
    expect(requests[0].path).toBe('/api/projects')

    fireEvent.click(screen.getByRole('checkbox', { name: 'Show cancelled projects' }))

    expect(await screen.findByText('Hillside cabin')).toBeInTheDocument()
    expect(requests).toHaveLength(2)
    expect(requests[1].path).toBe('/api/projects?includeCancelled=true')
  })

  it('goes back to the active list when the toggle is switched off again', async () => {
    const requests = renderPage(
      'Client',
      apiResponse(200, projects()),
      apiResponse(200, projects()),
    )

    await screen.findByText('Beachfront villa')
    const toggle = screen.getByRole('checkbox', { name: 'Show cancelled projects' })

    fireEvent.click(toggle)
    await waitFor(() => expect(requests).toHaveLength(2))
    expect(requests[1].path).toBe('/api/projects?includeCancelled=true')

    fireEvent.click(toggle)
    await waitFor(() => expect(requests).toHaveLength(3))
    expect(requests[2].path).toBe('/api/projects')
  })
})
