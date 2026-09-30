import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { ArchitectDashboard } from '@/components/dashboard/ArchitectDashboard'
import { apiResponse, stubRoutes } from '@/test/fake-fetch'
import {
  ARCHITECT_PATHS,
  VILLA_ID,
  architectDesign,
  architectProjects,
  architectRoutes,
} from '@/test/dashboard-fixtures'
import { signInAs } from '@/test/sign-in'

/**
 * The Architect's dashboard (US-21 AC-2): their assigned projects and the revisions
 * Clients have sent back — two requests to two services, joined on the project id.
 */
function renderDashboard(routes: Record<string, Response | Error> = architectRoutes()) {
  signInAs('Architect')
  const requests = stubRoutes(routes)

  render(
    <MemoryRouter>
      <AuthProvider>
        <ArchitectDashboard />
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

/** A summary tile, found by its label inside the summary — a column header can share the words. */
function tile(label: string): HTMLElement {
  return within(screen.getByRole('region', { name: 'Your assigned projects at a glance' }))
    .getByText(label)
    .closest('div') as HTMLElement
}

/** One project's row in the assigned projects table. */
function row(name: string): HTMLElement {
  return screen.getByRole('link', { name }).closest('tr') as HTMLElement
}

/** The panel for one pending revision, found by its document heading. */
function revision(heading: string): HTMLElement {
  return screen.getByRole('heading', { name: heading }).closest('[data-slot="card"]') as HTMLElement
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('ArchitectDashboard', () => {
  it('asks each service for the slice it owns, and nothing else', async () => {
    const requests = renderDashboard()

    await screen.findByRole('link', { name: 'Beachfront villa' })
    await screen.findByText('GroundFloorPlan · version 1')

    expect(requests.map((request) => request.path).sort()).toEqual(Object.values(ARCHITECT_PATHS).sort())
  })

  describe('assigned projects', () => {
    it('counts them in a tile', async () => {
      renderDashboard()

      await screen.findByRole('link', { name: 'Beachfront villa' })

      expect(tile('Assigned projects')).toHaveTextContent('2')
    })

    it('lists each with its place and status, linked to the project', async () => {
      renderDashboard()

      await screen.findByRole('link', { name: 'Beachfront villa' })

      const villa = row('Beachfront villa')
      expect(within(villa).getByRole('link', { name: 'Beachfront villa' })).toHaveAttribute(
        'href',
        `/projects/${VILLA_ID}`,
      )
      expect(within(villa).getByText('Galle')).toBeInTheDocument()
      expect(within(villa).getByText('Designing')).toBeInTheDocument()
      expect(within(row('Hilltop cottage')).getByText('Construction')).toBeInTheDocument()
    })

    it('says so when none has been assigned', async () => {
      renderDashboard(
        architectRoutes({
          projects: apiResponse(200, architectProjects([])),
          design: apiResponse(200, { pendingRevisionCount: 0, revisions: [] }),
        }),
      )

      expect(await screen.findByText('No project has been assigned to you yet.', { exact: false })).toBeInTheDocument()
      expect(tile('Assigned projects')).toHaveTextContent('0')
    })

    it('marks the projects that have revisions waiting, and only those', async () => {
      renderDashboard()

      await screen.findByRole('link', { name: 'Beachfront villa' })

      expect(await within(row('Beachfront villa')).findByText('2 to make')).toBeInTheDocument()
      expect(within(row('Hilltop cottage')).getByText('None')).toBeInTheDocument()
    })
  })

  describe('pending revisions', () => {
    it('counts them in a tile', async () => {
      renderDashboard()

      await screen.findByText('Sent back by clients')

      expect(tile('Pending revisions')).toHaveTextContent('2')
    })

    it('lists each with the document, the version and the project it belongs to', async () => {
      renderDashboard()

      await screen.findByText('GroundFloorPlan · version 1')
      const plan = revision('GroundFloorPlan · version 1')

      // The Design Service holds project ids only; the name comes from the Project Service.
      expect(within(plan).getByText('Beachfront villa')).toBeInTheDocument()
      expect(within(revision('Elevations · version 3')).getByText('Beachfront villa')).toBeInTheDocument()
    })

    it('quotes what the Client asked to change', async () => {
      renderDashboard()

      await screen.findByText('GroundFloorPlan · version 1')

      expect(within(revision('GroundFloorPlan · version 1')).getByText(/Move the stairs to the east wall/)).toBeInTheDocument()
    })

    it('says nothing about a comment when the Client left none', async () => {
      renderDashboard()

      await screen.findByText('Elevations · version 3')

      expect(within(revision('Elevations · version 3')).queryByText(/The client asked/)).not.toBeInTheDocument()
    })

    it('says when it was asked for and how long it has waited', async () => {
      renderDashboard()

      await screen.findByText('GroundFloorPlan · version 1')

      expect(within(revision('GroundFloorPlan · version 1')).getByText(/Requested .*2026 · waiting \d+ days?/)).toBeInTheDocument()
    })

    it('leads to where the next version is uploaded', async () => {
      renderDashboard()

      await screen.findByText('GroundFloorPlan · version 1')

      expect(
        within(revision('GroundFloorPlan · version 1')).getByRole('link', { name: 'Upload the next version' }),
      ).toHaveAttribute('href', `/projects/${VILLA_ID}/designs`)
    })

    it('keeps the order the service gave them in, longest waiting first', async () => {
      renderDashboard()

      await screen.findByText('GroundFloorPlan · version 1')

      const headings = screen
        .getAllByRole('heading', { level: 3 })
        .map((heading) => heading.textContent)
        .filter((text) => text?.includes('· version'))

      expect(headings).toEqual(['GroundFloorPlan · version 1', 'Elevations · version 3'])
    })

    it('says nothing is waiting, and shows zero, when nothing has been sent back', async () => {
      renderDashboard(
        architectRoutes({ design: apiResponse(200, { pendingRevisionCount: 0, revisions: [] }) }),
      )

      expect(await screen.findByText('Nothing to redo')).toBeInTheDocument()
      expect(tile('Pending revisions')).toHaveTextContent('0')
      expect(screen.getByText('Nothing sent back')).toBeInTheDocument()
    })

    it('says a revision asked for today was asked for today, not that it has waited 0 days', async () => {
      const design = architectDesign()
      design.revisions[0].requestedAt = new Date().toISOString().replace('Z', '')
      renderDashboard(architectRoutes({ design: apiResponse(200, design) }))

      await screen.findByText('GroundFloorPlan · version 1')

      const line = within(revision('GroundFloorPlan · version 1')).getByText(/Requested /)
      expect(line).toHaveTextContent('asked for today')
      expect(line).not.toHaveTextContent('0 days')
    })

    it('writes one day as a day, not as days', async () => {
      const yesterday = new Date(Date.now() - 36 * 60 * 60 * 1000).toISOString().replace('Z', '')
      const design = architectDesign()
      design.revisions[0].requestedAt = yesterday
      renderDashboard(architectRoutes({ design: apiResponse(200, design) }))

      await screen.findByText('GroundFloorPlan · version 1')

      expect(within(revision('GroundFloorPlan · version 1')).getByText(/waiting 1 day$/)).toBeInTheDocument()
    })
  })

  describe('when one service cannot answer', () => {
    it('says so and keeps the assigned projects', async () => {
      renderDashboard(architectRoutes({ design: apiResponse(500, { title: 'Boom' }) }))

      expect(await screen.findByRole('alert')).toHaveTextContent('Pending revisions could not be loaded right now.')

      expect(await screen.findByRole('link', { name: 'Beachfront villa' })).toBeInTheDocument()
      expect(tile('Assigned projects')).toHaveTextContent('2')
      // A dash, not a zero: a failure is not "nothing sent back".
      expect(tile('Pending revisions')).toHaveTextContent('—')
      expect(within(row('Beachfront villa')).getByLabelText('Unavailable')).toBeInTheDocument()
    })

    it('shows the reason the Design Service gave when it cannot ask the Project Service', async () => {
      renderDashboard(
        architectRoutes({
          design: apiResponse(502, {
            title: 'Projects could not be listed',
            detail: 'Your projects could not be looked up right now.',
          }),
        }),
      )

      expect(await screen.findByRole('alert')).toHaveTextContent('Your projects could not be looked up right now.')
    })

    it('still lists the revisions, naming the project by a short id, when the projects cannot be read', async () => {
      renderDashboard(architectRoutes({ projects: apiResponse(500, { title: 'Boom' }) }))

      expect(await screen.findByRole('alert')).toHaveTextContent('Your assigned projects could not be loaded right now.')

      const plan = revision('GroundFloorPlan · version 1')
      // Better a short id than a blank: the revision is still something to act on.
      expect(await within(plan).findByText(VILLA_ID.slice(0, 8))).toBeInTheDocument()
      expect(tile('Assigned projects')).toHaveTextContent('—')
    })
  })

  it('leads the assigned projects tile to the projects page', async () => {
    renderDashboard()

    await screen.findByRole('link', { name: 'Beachfront villa' })

    expect(screen.getByText('Assigned projects').closest('a')).toHaveAttribute('href', '/projects')
  })
})
