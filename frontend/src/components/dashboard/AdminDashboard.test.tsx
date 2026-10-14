import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { AdminDashboard } from '@/components/dashboard/AdminDashboard'
import { apiResponse, stubRoutes } from '@/test/fake-fetch'
import { ADMIN_PATHS, adminRoutes } from '@/test/dashboard-fixtures'
import { signInAs } from '@/test/sign-in'

/**
 * The Admin's dashboard (US-21 AC-4): system-wide counts of users and projects, and the
 * reports to open — two requests to two services.
 */
function renderDashboard(routes: Record<string, Response | Error> = adminRoutes()) {
  signInAs('Admin')
  const requests = stubRoutes(routes)

  render(
    <MemoryRouter>
      <AuthProvider>
        <AdminDashboard />
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

/** A summary tile, found by its label inside the summary. */
function tile(label: string): HTMLElement {
  return within(screen.getByRole('region', { name: 'The whole platform at a glance' }))
    .getByText(label)
    .closest('a') as HTMLElement
}

/** One of the breakdown panels, by its heading. */
function panel(heading: string): HTMLElement {
  return screen.getByRole('heading', { name: heading }).closest('[data-slot="card"]') as HTMLElement
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('AdminDashboard', () => {
  it('asks each service for the slice it owns, and nothing else', async () => {
    const requests = renderDashboard()

    await screen.findByText('10 still active')

    expect(requests.map((request) => request.path).sort()).toEqual(Object.values(ADMIN_PATHS).sort())
  })

  describe('users', () => {
    it('counts every account in a tile, and how many can sign in', async () => {
      renderDashboard()

      await screen.findByText('9 active · 1 deactivated')

      expect(tile('Users')).toHaveTextContent('10')
    })

    it('counts the accounts in each role, including the deactivated ones', async () => {
      renderDashboard()

      await screen.findByText('9 active · 1 deactivated')
      const roles = within(panel('Users by role'))

      expect(roles.getByText('Client').closest('div')).toHaveTextContent('6')
      expect(roles.getByText('Architect').closest('div')).toHaveTextContent('2')
      expect(roles.getByText('Project Manager').closest('div')).toHaveTextContent('1')
      expect(roles.getByText('Admin').closest('div')).toHaveTextContent('1')
    })

    it('leads to the users page', async () => {
      renderDashboard()

      await screen.findByText('9 active · 1 deactivated')

      expect(tile('Users')).toHaveAttribute('href', '/admin/users')
    })
  })

  describe('projects', () => {
    it('counts every project in a tile, and how many are still active', async () => {
      renderDashboard()

      await screen.findByText('10 still active')

      expect(tile('Projects')).toHaveTextContent('16')
    })

    it('counts the projects in each status, including a status nothing is in', async () => {
      renderDashboard(
        adminRoutes({
          projects: apiResponse(200, {
            totalCount: 3,
            groups: [
              { status: 'Pending', count: 3 },
              { status: 'Designing', count: 0 },
              { status: 'DesignApproved', count: 0 },
              { status: 'Construction', count: 0 },
              { status: 'Completed', count: 0 },
              { status: 'Cancelled', count: 0 },
            ],
          }),
        }),
      )

      await screen.findByText('3 still active')
      const statuses = within(panel('Projects by status'))

      expect(statuses.getByText('Pending').closest('div')).toHaveTextContent('3')
      // Present at zero, so the shape does not change with the data.
      expect(statuses.getByText('Designing').closest('div')).toHaveTextContent('0')
      expect(statuses.getAllByRole('definition')).toHaveLength(6)
    })

    it('does not count completed or cancelled projects as active', async () => {
      renderDashboard()

      // 3 + 4 + 1 + 2 active; the 5 completed and 1 cancelled are done with.
      expect(await screen.findByText('10 still active')).toBeInTheDocument()
    })

    it('leads to the project status report', async () => {
      renderDashboard()

      await screen.findByText('10 still active')

      expect(tile('Projects')).toHaveAttribute('href', '/admin/reports/project-status')
    })
  })

  describe('reports', () => {
    it('links to every report the Admin can open', async () => {
      renderDashboard()

      const reports = within(panel('Reports'))

      expect(reports.getByRole('link', { name: 'Project report' })).toHaveAttribute(
        'href',
        '/admin/reports/project-status',
      )
      expect(reports.getByRole('link', { name: 'Design report' })).toHaveAttribute(
        'href',
        '/admin/reports/design-approval',
      )
      expect(reports.getByRole('link', { name: 'Build & payment' })).toHaveAttribute(
        'href',
        '/reports/construction-payment',
      )
    })

    it('counts the report pages available, and says reports are not stored', async () => {
      renderDashboard()

      await screen.findByText('10 still active')

      // What the count is of: the pages the Admin can open. Nothing stores a report, so the
      // tile says "available" and "not stored" rather than pretend to count reports made.
      const summary = within(screen.getByRole('region', { name: 'The whole platform at a glance' }))
      expect(summary.getByText('Reports available').closest('div')).toHaveTextContent('3')
      expect(summary.getByText('Generated on demand, not stored')).toBeInTheDocument()
    })

    it('matches the count to the links beneath it, so the two cannot drift', async () => {
      renderDashboard()

      await screen.findByText('10 still active')

      const linked = within(panel('Reports')).getAllByRole('link')
      const summary = within(screen.getByRole('region', { name: 'The whole platform at a glance' }))
      expect(summary.getByText('Reports available').closest('div')).toHaveTextContent(String(linked.length))
    })

    it('leaves the reports tile as a plain readout, because the links are in the panel', async () => {
      renderDashboard()

      await screen.findByText('10 still active')

      // Two linked tiles, Users and Projects; the reports tile is not a link.
      const summary = within(screen.getByRole('region', { name: 'The whole platform at a glance' }))
      expect(summary.getAllByRole('link')).toHaveLength(2)
    })

    it('shows the reports even while the counts are still loading, since they need no data', () => {
      renderDashboard()

      expect(within(panel('Reports')).getAllByRole('link')).toHaveLength(3)
    })
  })

  describe('when one service cannot answer', () => {
    it('says so and keeps the other services counts', async () => {
      renderDashboard(adminRoutes({ users: apiResponse(500, { title: 'Boom' }) }))

      expect(await screen.findByRole('alert')).toHaveTextContent('User counts could not be loaded right now.')

      // The projects are unaffected, and the users tile is a dash, not a zero.
      expect(await screen.findByText('10 still active')).toBeInTheDocument()
      expect(tile('Projects')).toHaveTextContent('16')
      expect(tile('Users')).toHaveTextContent('—')
      expect(within(panel('Users by role')).getByText('Not available.')).toBeInTheDocument()
    })

    it('keeps the user counts when the projects cannot be read', async () => {
      renderDashboard(adminRoutes({ projects: new Error('connection refused') }))

      expect(await screen.findByRole('alert')).toHaveTextContent('Project counts could not be loaded right now.')
      expect(await screen.findByText('9 active · 1 deactivated')).toBeInTheDocument()
      expect(tile('Projects')).toHaveTextContent('—')
    })
  })
})
