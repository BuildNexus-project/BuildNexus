import { render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { ProjectManagerDashboard } from '@/components/dashboard/ProjectManagerDashboard'
import { formatDay } from '@/lib/milestone-dates'
import { apiResponse, stubRoutes } from '@/test/fake-fetch'
import {
  COTTAGE_ID,
  PM_PATHS,
  VILLA_ID,
  pmConstruction,
  pmRoutes,
} from '@/test/dashboard-fixtures'
import { signInAs } from '@/test/sign-in'

/**
 * The Project Manager's dashboard (US-21 AC-3): the builds under way and the milestones still
 * to finish, on the projects they are assigned to. One request — the Construction Service asks
 * the Project Service who the projects are, and names them in its own answer.
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

/** The row for one build in the active construction table. */
function buildRow(project: string): HTMLElement {
  return within(screen.getByRole('region', { name: 'Active construction' }))
    .getByText(project)
    .closest('tr') as HTMLElement
}

/** One milestone's entry in the milestones due list, by its name. */
function milestone(name: string): HTMLElement {
  return within(screen.getByRole('region', { name: 'Milestones due' })).getByText(name).closest('li') as HTMLElement
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('ProjectManagerDashboard', () => {
  it('asks the Construction Service once, and nothing else', async () => {
    const requests = renderDashboard()

    await screen.findByRole('link', { name: 'Hilltop cottage' })

    expect(requests.map((request) => request.path)).toEqual(Object.values(PM_PATHS))
  })

  describe('active construction', () => {
    it('counts the builds under way in a tile', async () => {
      renderDashboard()

      await screen.findByRole('link', { name: 'Hilltop cottage' })

      expect(tile('Active builds')).toHaveTextContent('2')
    })

    it('lists each build by name with its phase, progress and what is left', async () => {
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

      await screen.findByRole('link', { name: 'Beachfront villa' })
      const villa = buildRow('Beachfront villa')

      expect(within(villa).getByText('Construction Complete')).toBeInTheDocument()
      expect(within(villa).getAllByRole('cell').at(-1)).toHaveTextContent('0')
    })

    it('links every build to its project, because every one is the Project Managers own', async () => {
      renderDashboard()

      await screen.findByRole('link', { name: 'Hilltop cottage' })

      expect(screen.getByRole('link', { name: 'Hilltop cottage' })).toHaveAttribute('href', `/projects/${COTTAGE_ID}`)
      expect(screen.getByRole('link', { name: 'Beachfront villa' })).toHaveAttribute('href', `/projects/${VILLA_ID}`)
    })

    it('says so, and says why, when nothing has been started on their projects', async () => {
      renderDashboard(
        pmRoutes({
          construction: apiResponse(200, {
            activeBuildCount: 0,
            activeBuilds: [],
            milestonesDue: { totalCount: 0, overdueCount: 0, milestones: [] },
          }),
        }),
      )

      expect(await screen.findByText('No builds under way')).toBeInTheDocument()
      expect(screen.getByText(/assigned to you/)).toBeInTheDocument()
      expect(tile('Active builds')).toHaveTextContent('0')
    })
  })

  describe('milestones due', () => {
    it('counts every outstanding milestone in a tile, not only the ones listed', async () => {
      renderDashboard()

      await screen.findByText('Walls')

      expect(tile('Milestones due')).toHaveTextContent('14')
    })

    it('says in the tile how many are overdue', async () => {
      renderDashboard()

      await screen.findByText('Walls')

      expect(tile('Milestones due')).toHaveTextContent('1 overdue')
    })

    it('does not mention overdue in the tile when nothing is', async () => {
      const construction = pmConstruction()
      construction.milestonesDue.overdueCount = 0
      renderDashboard(pmRoutes({ construction: apiResponse(200, construction) }))

      await screen.findByText('Walls')

      expect(tile('Milestones due')).not.toHaveTextContent('overdue')
    })

    it('still explains in the tile that due means not yet completed', async () => {
      renderDashboard()

      expect(await screen.findByText(/Not yet completed, on active builds/)).toBeInTheDocument()
    })

    it('lists each milestone with its project and where it stands', async () => {
      renderDashboard()

      await screen.findByText('Walls')

      expect(milestone('Walls')).toHaveTextContent('In Progress')
      expect(milestone('Walls')).toHaveTextContent('Hilltop cottage')
      expect(milestone('Painting')).toHaveTextContent('Not Started')
      expect(milestone('Painting')).toHaveTextContent('Beachfront villa')
    })

    it('says a late milestone is overdue, with the day it was due', async () => {
      renderDashboard()

      await screen.findByText('Walls')

      expect(milestone('Walls')).toHaveTextContent(`Overdue — was due ${formatDay('2026-09-20')}`)
    })

    it('shows a dated milestone that is not late as simply due, without the overdue wording', async () => {
      renderDashboard()

      await screen.findByText('Roof')

      expect(milestone('Roof')).toHaveTextContent(`Due ${formatDay('2026-11-03')}`)
      expect(milestone('Roof')).not.toHaveTextContent('Overdue')
    })

    it('shows nothing about a date for a milestone that has none', async () => {
      renderDashboard()

      await screen.findByText('Painting')

      expect(milestone('Painting')).not.toHaveTextContent(/Due|Overdue/)
    })

    it('leads the list with how many are overdue', async () => {
      renderDashboard()

      await screen.findByText('Walls')

      expect(within(screen.getByRole('region', { name: 'Milestones due' })).getByText('1 overdue milestone')).toBeInTheDocument()
    })

    it('keeps the order the service gave: dated first, soonest first', async () => {
      renderDashboard()

      await screen.findByText('Walls')

      const names = within(screen.getByRole('region', { name: 'Milestones due' }))
        .getAllByRole('listitem')
        .map((item) => item.querySelector('span')?.textContent)

      expect(names).toEqual(['Walls', 'Roof', 'Painting'])
    })

    it('says how many of the total are shown when there are more than the list holds', async () => {
      renderDashboard()

      expect(await screen.findByText(/Showing 3 of 14/)).toBeInTheDocument()
    })

    it('does not claim a partial list when it is the whole of it', async () => {
      const construction = pmConstruction()
      construction.milestonesDue.totalCount = 3
      renderDashboard(pmRoutes({ construction: apiResponse(200, construction) }))

      await screen.findByText('Walls')

      expect(screen.queryByText(/Showing/)).not.toBeInTheDocument()
    })

    it('says so when everything on the running builds is done', async () => {
      const construction = pmConstruction()
      construction.milestonesDue = { totalCount: 0, overdueCount: 0, milestones: [] }
      renderDashboard(pmRoutes({ construction: apiResponse(200, construction) }))

      expect(await screen.findByText('Nothing outstanding')).toBeInTheDocument()
      expect(tile('Milestones due')).toHaveTextContent('0')
    })
  })

  describe('when the service cannot answer', () => {
    it('says so, and shows dashes rather than zeros', async () => {
      renderDashboard(pmRoutes({ construction: apiResponse(500, { title: 'Boom' }) }))

      expect(await screen.findByRole('alert')).toHaveTextContent('Active construction could not be loaded right now.')
      expect(tile('Active builds')).toHaveTextContent('—')
      expect(tile('Milestones due')).toHaveTextContent('—')
    })

    it('shows the reason when the Construction Service cannot reach the Project Service', async () => {
      // An outage there must never read as "nothing under way" — it is a 502 with a reason.
      renderDashboard(
        pmRoutes({
          construction: apiResponse(502, {
            title: 'Projects could not be listed',
            detail: 'Your projects could not be looked up right now, so your construction summary cannot be shown.',
          }),
        }),
      )

      expect(await screen.findByRole('alert')).toHaveTextContent('so your construction summary cannot be shown')
      expect(tile('Active builds')).toHaveTextContent('—')
    })
  })

  it('leads both tiles to the report where builds and billing are read together', async () => {
    renderDashboard()

    await screen.findByRole('link', { name: 'Hilltop cottage' })

    expect(tile('Active builds')).toHaveAttribute('href', '/reports/construction-payment')
    expect(tile('Milestones due')).toHaveAttribute('href', '/reports/construction-payment')
  })
})
