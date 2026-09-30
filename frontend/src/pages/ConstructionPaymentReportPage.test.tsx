import { fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { AuthProvider } from '@/auth/AuthProvider'
import { ConstructionPaymentReportPage } from '@/pages/ConstructionPaymentReportPage'
import { apiResponse, type RecordedRequest } from '@/test/fake-fetch'
import type { Role } from '@/lib/roles'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'
const PM_ID = '22222222-2222-4222-8222-222222222222'
const FIRST_PROJECT = '11111111-1111-4111-8111-111111111111'
const SECOND_PROJECT = '33333333-3333-4333-8333-333333333333'

/** One line of the construction half, as the service returns it. */
function progressRow(overrides: Record<string, unknown> = {}) {
  return {
    projectId: FIRST_PROJECT,
    phaseStatus: 'Started',
    totalMilestones: 4,
    completedMilestones: 1,
    inProgressMilestones: 2,
    notStartedMilestones: 1,
    progressPercent: 25,
    ...overrides,
  }
}

/** The payment half, as the service returns it. */
function summary(overrides: Record<string, unknown> = {}) {
  return {
    totalInvoiced: 1000,
    totalCollected: 400,
    totalOutstanding: 600,
    invoiceCount: 2,
    paymentCount: 1,
    fromUtc: null,
    toUtc: null,
    ...overrides,
  }
}

function signInAs(role: Role) {
  const claims = {
    sub: PM_ID,
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

/**
 * Renders the page for a Project Manager.
 *
 * The two halves load concurrently from two services, so the stub is keyed by path rather
 * than by call order — which is also the point being made: neither request depends on the
 * other, and their order is not something this page controls.
 */
function renderPage(
  responses: {
    progress?: Response | Error
    summaries?: Array<Response | Error>
    /** The project listing the names come from; refused by default, as it is for a PM not on the project. */
    projects?: Response | Error
  } = {},
): RecordedRequest[] {
  signInAs('ProjectManager')

  const progress = responses.progress ?? apiResponse(200, [progressRow()])
  const summaries = responses.summaries ?? [apiResponse(200, summary())]
  const projects = responses.projects ?? apiResponse(403, { title: 'Forbidden' })

  const requests: RecordedRequest[] = []
  let summaryCall = 0

  vi.stubGlobal('fetch', (path: string, init: RequestInit = {}) => {
    requests.push({ path, method: init.method, body: undefined })

    if (path.startsWith('/api/construction/reports/progress')) {
      return progress instanceof Error ? Promise.reject(progress) : Promise.resolve(progress)
    }

    // Routed explicitly rather than falling through: the name lookup is a third
    // request, and letting it draw from the summaries queue would silently shift
    // which summary every later assertion sees.
    if (path.startsWith('/api/projects')) {
      return projects instanceof Error ? Promise.reject(projects) : Promise.resolve(projects)
    }

    const answer = summaries[Math.min(summaryCall++, summaries.length - 1)]

    return answer instanceof Error ? Promise.reject(answer) : Promise.resolve(answer)
  })

  render(
    <MemoryRouter>
      <AuthProvider>
        <ConstructionPaymentReportPage />
      </AuthProvider>
    </MemoryRouter>,
  )

  return requests
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('ConstructionPaymentReportPage', () => {
  it('shows each active project with its percentage and milestone breakdown', async () => {
    // AC-1: per-project overall progress and the breakdown behind it.
    renderPage({
      progress: apiResponse(200, [
        progressRow(),
        progressRow({
          projectId: SECOND_PROJECT,
          phaseStatus: 'Completed',
          totalMilestones: 2,
          completedMilestones: 2,
          inProgressMilestones: 0,
          notStartedMilestones: 0,
          progressPercent: 100,
        }),
      ]),
    })

    const panel = await screen.findByTestId('construction-report')

    expect(await within(panel).findByText('25.00%')).toBeInTheDocument()
    expect(within(panel).getByText('100.00%')).toBeInTheDocument()
    // The breakdown, not just the percentage — what is outstanding, beside how far along.
    expect(within(panel).getByText('1 / 4')).toBeInTheDocument()
    expect(within(panel).getByText('In Construction')).toBeInTheDocument()
  })

  it('shows a build that has not started as such', async () => {
    renderPage({
      progress: apiResponse(200, [
        progressRow({ phaseStatus: null, completedMilestones: 0, progressPercent: 0 }),
      ]),
    })

    const panel = await screen.findByTestId('construction-report')

    // The phase badge, unambiguous now that the milestone count column reads "To do".
    expect(await within(panel).findByText('Not started')).toBeInTheDocument()
    expect(within(panel).getByText('0.00%')).toBeInTheDocument()
  })

  it('explains an empty portfolio rather than showing a bare table', async () => {
    renderPage({ progress: apiResponse(200, []) })

    const panel = await screen.findByTestId('construction-report')

    expect(await within(panel).findByText(/No active project has milestones defined yet/i))
      .toBeInTheDocument()
  })

  it('shows the payment totals on the payments tab', async () => {
    // AC-2: invoiced, collected and outstanding across all projects.
    renderPage()

    await screen.findByTestId('construction-report')
    fireEvent.click(screen.getByRole('tab', { name: 'Payments' }))

    const panel = await screen.findByTestId('payment-report')

    expect(await within(panel).findByText('1,000.00')).toBeInTheDocument()
    expect(within(panel).getByText('400.00')).toBeInTheDocument()
    expect(within(panel).getByText('600.00')).toBeInTheDocument()
    // The counts, so a total made of two invoices reads differently from one of fifty.
    expect(within(panel).getByText('2 invoices')).toBeInTheDocument()
    expect(within(panel).getByText('1 payment')).toBeInTheDocument()
  })

  it('applies a date range and covers the whole of the chosen end day', async () => {
    const requests = renderPage({
      summaries: [
        apiResponse(200, summary()),
        apiResponse(200, summary({
          totalInvoiced: 300,
          totalCollected: 100,
          totalOutstanding: null,
          fromUtc: '2026-09-01T00:00:00Z',
          toUtc: '2026-10-01T00:00:00Z',
        })),
      ],
    })

    await screen.findByTestId('construction-report')
    fireEvent.click(screen.getByRole('tab', { name: 'Payments' }))
    await screen.findByTestId('payment-report')

    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-09-01' } })
    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-09-30' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }))

    const filtered = await screen.findByText('300.00')
    expect(filtered).toBeInTheDocument()

    const query = requests.map((request) => request.path).find((path) => path.includes('fromUtc'))
    expect(query).toContain('fromUtc=2026-09-01T00%3A00%3A00Z')
    // The end bound is the start of the next day, so the 30th is included in full rather
    // than cut at its first instant.
    expect(query).toContain('toUtc=2026-10-01T00%3A00%3A00Z')
  })

  it('does not show an outstanding figure for a filtered range', async () => {
    // Invoiced is scoped by invoice date and collected by payment date, so a number here
    // could read as debt that does not exist.
    renderPage({
      summaries: [
        apiResponse(200, summary({
          totalOutstanding: null,
          fromUtc: '2026-09-01T00:00:00Z',
          toUtc: '2026-10-01T00:00:00Z',
        })),
      ],
    })

    await screen.findByTestId('construction-report')
    fireEvent.click(screen.getByRole('tab', { name: 'Payments' }))

    const panel = await screen.findByTestId('payment-report')

    expect(await within(panel).findByText('Only shown for the full ledger')).toBeInTheDocument()
    expect(within(panel).getByText('—')).toBeInTheDocument()
  })

  it('surfaces the service’s reason when a range is rejected', async () => {
    renderPage({
      summaries: [
        apiResponse(200, summary()),
        apiResponse(400, {
          title: 'Invalid date range.',
          detail: 'fromUtc must be earlier than toUtc.',
          status: 400,
        }),
      ],
    })

    await screen.findByTestId('construction-report')
    fireEvent.click(screen.getByRole('tab', { name: 'Payments' }))
    await screen.findByTestId('payment-report')

    fireEvent.change(screen.getByLabelText('From'), { target: { value: '2026-10-01' } })
    fireEvent.change(screen.getByLabelText('To'), { target: { value: '2026-09-01' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply' }))

    expect(await screen.findByText('fromUtc must be earlier than toUtc.')).toBeInTheDocument()
  })

  it('keeps one half on screen when the other service fails', async () => {
    // The two halves come from two services, each queried independently. Payment Service
    // being unreachable must not blank the construction figures — that is the whole reason
    // each half owns its own error state.
    renderPage({ summaries: [apiResponse(500, { title: 'Server error' })] })

    const construction = await screen.findByTestId('construction-report')
    expect(await within(construction).findByText('25.00%')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('tab', { name: 'Payments' }))

    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })

  it('reads each report from its own service', async () => {
    // The architecture the story turns on: two endpoints, each over its own schema, joined
    // here rather than by one service reaching into the other's database.
    const requests = renderPage()

    await screen.findByTestId('construction-report')

    expect(requests.map((request) => request.path)).toEqual(
      expect.arrayContaining([
        '/api/construction/reports/progress',
        '/api/payments/reports/summary',
      ]),
    )
  })
})

describe('naming the projects in the construction half (US-19)', () => {
  it('shows the project name when the caller may see it', async () => {
    // The report comes from the Construction Service, which knows ids and nothing
    // else. A bare id tells the reader nothing about which build they are looking at.
    renderPage({
      projects: apiResponse(200, [
        { id: FIRST_PROJECT, clientId: PM_ID, name: 'Seaside Villa', location: 'Galle', status: 'Construction', createdAt: '2026-09-01T09:00:00', updatedAt: '2026-09-01T09:00:00' },
      ]),
    })

    expect(await screen.findByRole('link', { name: 'Seaside Villa' })).toBeInTheDocument()
  })

  it('falls back to the short id when the name cannot be resolved', async () => {
    // A Project Manager only sees projects they are on, so the listing can legitimately
    // omit a row the report still includes. It must degrade, not disappear.
    renderPage({ projects: apiResponse(403, { title: 'Forbidden' }) })

    expect(await screen.findByRole('link', { name: FIRST_PROJECT.slice(0, 8) })).toBeInTheDocument()
  })

  it('still renders the progress figures when the name lookup fails', async () => {
    // The names are decoration on top of the report; losing them must not cost the
    // reader the numbers they came for.
    renderPage({ projects: new TypeError('Failed to fetch') })

    const panel = await screen.findByTestId('construction-report')
    expect(within(panel).getByText('25.00%')).toBeInTheDocument()
  })

  it('carries the full id on the link for a reader who needs it', async () => {
    renderPage({
      projects: apiResponse(200, [
        { id: FIRST_PROJECT, clientId: PM_ID, name: 'Seaside Villa', location: 'Galle', status: 'Construction', createdAt: '2026-09-01T09:00:00', updatedAt: '2026-09-01T09:00:00' },
      ]),
    })

    expect(await screen.findByRole('link', { name: 'Seaside Villa' })).toHaveAttribute(
      'title',
      FIRST_PROJECT,
    )
  })
})

describe('saying what "active" means (US-19)', () => {
  it('states the rule the report actually filters on', async () => {
    renderPage()

    const panel = await screen.findByTestId('construction-report')

    // The three build states that appear, and the one that removes a project.
    expect(within(panel).getByText(/design is approved/i)).toBeInTheDocument()
    expect(within(panel).getByText(/not yet been handed over/i)).toBeInTheDocument()
    expect(within(panel).getByText(/milestones are planned/i)).toBeInTheDocument()
  })

  it('does not claim a rule when there is nothing to describe', async () => {
    // An empty report already explains itself; repeating the definition beneath it
    // would describe a selection the reader cannot see.
    renderPage({ progress: apiResponse(200, []) })

    const panel = await screen.findByTestId('construction-report')

    expect(within(panel).queryByText(/not yet been handed over/i)).not.toBeInTheDocument()
  })
})
