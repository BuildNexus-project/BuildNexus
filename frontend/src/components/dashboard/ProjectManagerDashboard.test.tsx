import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { ProjectManagerDashboard } from '@/components/dashboard/ProjectManagerDashboard'
import { apiResponse, stubRoutes } from '@/test/fake-fetch'
import {
  COTTAGE_ID,
  PM_PATHS,
  STRANGER_ID,
  pmConstruction,
  pmRoutes,
} from '@/test/dashboard-fixtures'
import { signInAs } from '@/test/sign-in'

/**
 * The Project Manager's dashboard (US-21 AC-3): the builds under way and the milestones
 * still to finish. The figures are one service's; the project names are another's, asked
 * for separately and best-effort.
 */
function renderDashboard(routes: Record<string, Response | Error> = pmRoutes()) {
  signInAs('ProjectManager')
  const requests = stubRoutes(routes)

  render(
    <MemoryRouter>
      <AuthProvider>
        <ProjectManagerDashboard />
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

/** A summary tile, found by its label inside the summary. */
function tile(label: string): HTMLElement {
  return within(screen.getByRole('region', { name: 'Construction at a glance' }))
    .getByText(label)
    .closest('a') as HTMLElement
}

/** The row for one build in the active construction table, by the project text in its first cell. */
function buildRow(project: string): HTMLElement {
  return within(screen.getByRole('region', { name: 'Active construction' }))
    .getByText(project)
    .closest('tr') as HTMLElement
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('ProjectManagerDashboard', () => {
  it('asks the Construction Service for the figures and the Project Service for the names', async () => {
    const requests = renderDashboard()

    await screen.findByRole('link', { name: 'Hilltop cottage' })

    expect(requests.map((request) => request.path).sort()).toEqual(Object.values(PM_PATHS).sort())
  })

  describe('active construction', () => {
    it('counts the builds under way in a tile', async () => {
      renderDashboard()

      await screen.findByRole('link', { name: 'Hilltop cottage' })

      expect(tile('Active builds')).toHaveTextContent('2')
    })

    it('lists each build with its phase, progress and what is left', async () => {
      renderDashboard()

      await screen.findByRole('link', { name: 'Hilltop cottage' })
      const cottage = buildRow('Hilltop cottage')

      expect(within(cottage).getByText('In Construction')).toBeInTheDocument()
      expect(within(cottage).getByRole('progressbar', { name: 'Hilltop cottage construction progress' })).toHaveAttribute(
        'aria-valuenow',
        '40',
      )
      expect(within(cottage).getByText('2 of 5 milestones')).toBeInTheDocument()
      expect(within(cottage).getAllByRole('cell').at(-1)).toHaveTextContent('3')
    })

    it('shows a finished build awaiting handover as complete, with nothing left', async () => {
      renderDashboard()

      await screen.findByRole('link', { name: 'Hilltop cottage' })
      const stranger = buildRow(STRANGER_ID.slice(0, 8))

      expect(within(stranger).getByText('Construction Complete')).toBeInTheDocument()
      expect(within(stranger).getAllByRole('cell').at(-1)).toHaveTextContent('0')
    })

    it('links a build to its project only when the Project Manager may open it', async () => {
      renderDashboard()

      await screen.findByRole('link', { name: 'Hilltop cottage' })

      // Assigned: a link. Not assigned: the Project Service would refuse the page, so plain text.
      expect(within(buildRow('Hilltop cottage')).getByRole('link', { name: 'Hilltop cottage' })).toHaveAttribute(
        'href',
        `/projects/${COTTAGE_ID}`,
      )
      expect(within(buildRow(STRANGER_ID.slice(0, 8))).queryByRole('link')).not.toBeInTheDocument()
    })

    it('names a build the Project Manager is not assigned to by a short id, rather than hiding it', async () => {
      renderDashboard()

      expect(await screen.findByText(STRANGER_ID.slice(0, 8))).toBeInTheDocument()
    })

    it('says so when nothing has been started', async () => {
      renderDashboard(
        pmRoutes({
          construction: apiResponse(200, {
            activeBuildCount: 0,
            activeBuilds: [],
            milestonesDue: { totalCount: 0, milestones: [] },
          }),
        }),
      )

      expect(await screen.findByText('No builds under way')).toBeInTheDocument()
      expect(tile('Active builds')).toHaveTextContent('0')
    })
  })

  describe('milestones due', () => {
    it('counts every outstanding milestone in a tile, not only the ones listed', async () => {
      renderDashboard()

      await screen.findByText('Walls')

      expect(tile('Milestones due')).toHaveTextContent('14')
    })

    it('lists the first few with the project each belongs to and where it stands', async () => {
      renderDashboard()

      await screen.findByText('Walls')
      const due = within(screen.getByRole('region', { name: 'Milestones due' }))

      expect(due.getByText('Walls').closest('li')).toHaveTextContent('In Progress')
      expect(due.getByText('Roof').closest('li')).toHaveTextContent('Not Started')
      expect(due.getByText('Walls').closest('li')).toHaveTextContent('Hilltop cottage')
    })

    it('says how many of the total are shown when there are more than the list holds', async () => {
      renderDashboard()

      expect(await screen.findByText(/Showing 2 of 14/)).toBeInTheDocument()
    })

    it('does not claim a partial list when it is the whole of it', async () => {
      const construction = pmConstruction()
      construction.milestonesDue.totalCount = 2
      renderDashboard(pmRoutes({ construction: apiResponse(200, construction) }))

      await screen.findByText('Walls')

      expect(screen.queryByText(/Showing/)).not.toBeInTheDocument()
    })

    it('says so when everything on the running builds is done', async () => {
      const construction = pmConstruction()
      construction.milestonesDue = { totalCount: 0, milestones: [] }
      renderDashboard(pmRoutes({ construction: apiResponse(200, construction) }))

      expect(await screen.findByText('Nothing outstanding')).toBeInTheDocument()
      expect(tile('Milestones due')).toHaveTextContent('0')
    })

    it('explains in the tile that due means not yet completed', async () => {
      // Milestones carry no due date, so the figure must not read as "overdue".
      renderDashboard()

      expect(await screen.findByText('Not yet completed, on active builds')).toBeInTheDocument()
    })
  })

  describe('when a service cannot answer', () => {
    it('says so, and shows dashes rather than zeros, when the figures cannot be read', async () => {
      renderDashboard(pmRoutes({ construction: apiResponse(500, { title: 'Boom' }) }))

      expect(await screen.findByRole('alert')).toHaveTextContent('Active construction could not be loaded right now.')
      expect(tile('Active builds')).toHaveTextContent('—')
      expect(tile('Milestones due')).toHaveTextContent('—')
    })

    it('shows every build by its short id, without an error, when only the names cannot be read', async () => {
      // A name is a convenience: a dashboard whose figures loaded is not made wrong by it.
      renderDashboard(pmRoutes({ projects: apiResponse(500, { title: 'Boom' }) }))

      // The assigned cottage is also named in the milestones list, so it appears more than once.
      expect((await screen.findAllByText(COTTAGE_ID.slice(0, 8))).length).toBeGreaterThan(0)
      expect(screen.getByText(STRANGER_ID.slice(0, 8))).toBeInTheDocument()
      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
      expect(tile('Active builds')).toHaveTextContent('2')
    })
  })

  it('leads both tiles to the report where builds and billing are read together', async () => {
    renderDashboard()

    await screen.findByRole('link', { name: 'Hilltop cottage' })

    expect(tile('Active builds')).toHaveAttribute('href', '/reports/construction-payment')
    expect(tile('Milestones due')).toHaveAttribute('href', '/reports/construction-payment')
  })
})
