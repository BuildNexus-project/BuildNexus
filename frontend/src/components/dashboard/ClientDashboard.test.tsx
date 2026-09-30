import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { ClientDashboard } from '@/components/dashboard/ClientDashboard'
import { apiResponse, stubRoutes } from '@/test/fake-fetch'
import {
  CLIENT_PATHS,
  COTTAGE_ID,
  VILLA_ID,
  clientProjects,
  clientRoutes,
} from '@/test/dashboard-fixtures'
import { signInAs } from '@/test/sign-in'

/**
 * The Client's dashboard (US-21 AC-1): active projects, design status, progress and
 * payments due — four requests to four services, joined on the project id.
 *
 * Each is served by a different service, so the tests stub each path separately and
 * check what the page makes of them together, and of any one going missing.
 */
function renderDashboard(routes: Record<string, Response | Error> = clientRoutes()) {
  signInAs('Client')
  const requests = stubRoutes(routes)

  render(
    <MemoryRouter>
      <AuthProvider>
        <ClientDashboard />
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

/**
 * A summary tile, found by its label: the link that holds the label and the figure. Looked
 * for inside the summary only — "Build progress" is a column of the table below it too.
 */
function tile(label: string): HTMLElement {
  return within(screen.getByRole('region', { name: 'Your projects at a glance' }))
    .getByText(label)
    .closest('a') as HTMLElement
}

/** One project's row in the active projects table. */
function row(name: string): HTMLElement {
  return screen.getByRole('link', { name }).closest('tr') as HTMLElement
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('ClientDashboard', () => {
  it('asks each service for the slice it owns, and nothing else', async () => {
    const requests = renderDashboard()

    await screen.findByRole('link', { name: 'Beachfront villa' })
    await screen.findByText('Nothing due')

    expect(requests.map((request) => request.path).sort()).toEqual(Object.values(CLIENT_PATHS).sort())
  })

  it('does not show figures before the services have answered', () => {
    renderDashboard()

    // Synchronously after render nothing has been answered: a placeholder, not a zero.
    expect(within(tile('Active projects')).getByText('…')).toBeInTheDocument()
    expect(screen.getByText('Loading your projects…')).toBeInTheDocument()
  })

  describe('active projects', () => {
    it('counts them in a tile', async () => {
      renderDashboard()

      await screen.findByRole('link', { name: 'Beachfront villa' })

      expect(tile('Active projects')).toHaveTextContent('2')
    })

    it('lists each with its name, place and status, linked to the project', async () => {
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

    it('says so, and offers to start one, when there are none', async () => {
      renderDashboard(clientRoutes({ projects: apiResponse(200, clientProjects([])) }))

      expect(await screen.findByText('You have no active projects.', { exact: false })).toBeInTheDocument()
      expect(screen.getByRole('link', { name: 'Start a project' })).toHaveAttribute('href', '/projects/new')
      expect(tile('Active projects')).toHaveTextContent('0')
    })
  })

  describe('design status', () => {
    it('shows where the design stands on each project', async () => {
      renderDashboard()

      await screen.findByRole('link', { name: 'Beachfront villa' })

      expect(await within(row('Beachfront villa')).findByText('Awaiting your review')).toBeInTheDocument()
      expect(await within(row('Hilltop cottage')).findByText('Approved')).toBeInTheDocument()
    })

    it('counts the designs waiting on the Client, and how many projects are approved', async () => {
      renderDashboard()

      await screen.findByText('1 of 2 approved')

      expect(tile('Designs awaiting your review')).toHaveTextContent('1')
    })
  })

  describe('build progress', () => {
    it('shows how far each planned build has got, as a progress bar', async () => {
      renderDashboard()

      await screen.findByRole('link', { name: 'Hilltop cottage' })
      const cottage = row('Hilltop cottage')
      const bar = await within(cottage).findByRole('progressbar', { name: 'Hilltop cottage build progress' })

      expect(bar).toHaveAttribute('aria-valuenow', '60')
      expect(within(cottage).getByText('3 of 5 milestones')).toBeInTheDocument()
    })

    it('says a project with no build plan is not planned yet, rather than showing zero', async () => {
      renderDashboard()

      await screen.findByRole('link', { name: 'Beachfront villa' })

      expect(await within(row('Beachfront villa')).findByText('Not planned yet')).toBeInTheDocument()
      expect(within(row('Beachfront villa')).queryByRole('progressbar')).not.toBeInTheDocument()
    })

    it('rolls the milestones up into one figure, weighted by milestones', async () => {
      renderDashboard()

      await screen.findByText('3 of 5 milestones done')

      expect(tile('Build progress')).toHaveTextContent('60%')
    })

    it('shows a dash and says no build is planned when nothing is', async () => {
      renderDashboard(clientRoutes({ construction: apiResponse(200, { projects: [] }) }))

      await screen.findByText('No build planned yet')

      expect(tile('Build progress')).toHaveTextContent('—')
    })
  })

  describe('payments due', () => {
    it('totals what is still owed and counts the unpaid invoices', async () => {
      renderDashboard()

      await screen.findByText('1 unpaid invoice')

      // Outstanding, not billed: 1,000.00 was invoiced and 250.00 is paid.
      expect(tile('Payments due')).toHaveTextContent(/750\.00/)
    })

    it('shows what is due beside the project it is due on', async () => {
      renderDashboard()

      await screen.findByText('1 unpaid invoice')

      expect(within(row('Hilltop cottage')).getByText(/750\.00/)).toBeInTheDocument()
      expect(within(row('Beachfront villa')).getByText('Nothing due')).toBeInTheDocument()
    })

    it('says nothing is owing, at zero, when nothing is', async () => {
      renderDashboard(
        clientRoutes({ payments: apiResponse(200, { totalDue: 0, invoiceCount: 0, invoices: [] }) }),
      )

      await screen.findByText('Nothing owing')

      expect(tile('Payments due')).toHaveTextContent(/0\.00/)
    })
  })

  describe('when one service cannot answer', () => {
    it('says so and keeps everything that did load', async () => {
      renderDashboard(clientRoutes({ payments: apiResponse(500, { title: 'Boom' }) }))

      expect(await screen.findByRole('alert')).toHaveTextContent('Payments due could not be loaded right now.')

      // The projects, design and progress are unaffected.
      expect(tile('Active projects')).toHaveTextContent('2')
      expect(await within(row('Beachfront villa')).findByText('Awaiting your review')).toBeInTheDocument()
      // The payments tile is a dash, not a zero: a failure is not "nothing owing".
      expect(tile('Payments due')).toHaveTextContent('—')
      expect(within(row('Hilltop cottage')).getAllByLabelText('Unavailable')).toHaveLength(1)
    })

    it('shows the reason the Design Service gave when it cannot ask the Project Service', async () => {
      renderDashboard(
        clientRoutes({
          design: apiResponse(502, {
            title: 'Projects could not be listed',
            detail: 'Your projects could not be looked up right now, so the design summary cannot be shown.',
          }),
        }),
      )

      expect(await screen.findByRole('alert')).toHaveTextContent('so the design summary cannot be shown')
      // The projects themselves are still listed.
      expect(await screen.findByRole('link', { name: 'Beachfront villa' })).toBeInTheDocument()
    })

    it('does not leave a page of nothing when the projects themselves cannot be read', async () => {
      renderDashboard(clientRoutes({ projects: new Error('connection refused') }))

      expect(await screen.findByRole('alert')).toHaveTextContent('Your projects could not be loaded right now.')
      expect(tile('Active projects')).toHaveTextContent('—')
      // The other slices still arrive and still show.
      expect(await screen.findByText('1 unpaid invoice')).toBeInTheDocument()
      expect(screen.queryByText('Loading your projects…')).not.toBeInTheDocument()
    })

    it('says each failure once', async () => {
      renderDashboard(
        clientRoutes({
          design: apiResponse(500, { title: 'Boom' }),
          construction: apiResponse(500, { title: 'Boom' }),
        }),
      )

      await screen.findByRole('link', { name: 'Beachfront villa' })
      await screen.findAllByRole('alert')

      expect(screen.getAllByRole('alert').map((alert) => alert.textContent)).toEqual([
        'Design status could not be loaded right now.',
        'Build progress could not be loaded right now.',
      ])
    })
  })

  it('links each tile to the page where that thing is done', async () => {
    renderDashboard()

    await screen.findByText('1 unpaid invoice')

    expect(tile('Active projects')).toHaveAttribute('href', '/projects')
    expect(tile('Build progress')).toHaveAttribute('href', '/progress')
    expect(tile('Payments due')).toHaveAttribute('href', '/my-costs')
  })

  it('joins the slices on the project id, not on the order they arrive in', async () => {
    // The cottage is listed first here, but the construction slice still names it by id.
    renderDashboard(
      clientRoutes({
        projects: apiResponse(200, {
          activeCount: 2,
          projects: [
            { id: COTTAGE_ID, name: 'Hilltop cottage', location: 'Kandy', status: 'Construction', updatedAt: '2026-09-05T09:00:00' },
            { id: VILLA_ID, name: 'Beachfront villa', location: 'Galle', status: 'Designing', updatedAt: '2026-09-04T09:00:00' },
          ],
        }),
      }),
    )

    await screen.findByRole('link', { name: 'Hilltop cottage' })
    const cottage = row('Hilltop cottage')

    expect(await within(cottage).findByRole('progressbar')).toHaveAttribute('aria-valuenow', '60')
    expect(within(row('Beachfront villa')).queryByRole('progressbar')).not.toBeInTheDocument()
  })
})
