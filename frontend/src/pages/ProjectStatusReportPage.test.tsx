import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import App from '@/App'
import { AuthProvider } from '@/auth/AuthProvider'
import { GROUP_PREVIEW_LIMIT, ProjectStatusReportPage } from '@/pages/ProjectStatusReportPage'
import type { ProjectStatusReport } from '@/lib/project-report-api'
import type { Role } from '@/lib/roles'
import { apiResponse, fileResponse } from '@/test/fake-fetch'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'
const ADMIN_ID = '1f9619ff-8b86-d011-b42d-00cf4fc964ff'

const VILLA = '11111111-1111-4111-8111-111111111111'
const COTTAGE = '22222222-2222-4222-8222-222222222222'
const TOWER = '33333333-3333-4333-8333-333333333333'

const STATUSES = [
  'Pending',
  'Designing',
  'DesignApproved',
  'Construction',
  'Completed',
  'Cancelled',
] as const

type Status = (typeof STATUSES)[number]

function row(id: string, name: string, status: Status, budget: number, createdAt: string) {
  return { id, name, location: 'Galle', status, budget, createdAt, updatedAt: createdAt }
}

/** A report with the given projects, grouped the way the service groups them. */
function reportOf(
  projects: ReturnType<typeof row>[],
  statuses: readonly Status[] = STATUSES,
): ProjectStatusReport {
  const groups = statuses.map((status) => {
    const inGroup = projects.filter((project) => project.status === status)

    return {
      status,
      count: inGroup.length,
      totalBudget: inGroup.reduce((sum, project) => sum + project.budget, 0),
      projects: inGroup,
    }
  })

  return {
    generatedAt: '2026-09-30T08:00:00Z',
    totalProjects: groups.reduce((sum, group) => sum + group.count, 0),
    totalBudget: groups.reduce((sum, group) => sum + group.totalBudget, 0),
    groups,
  }
}

const PIPELINE = [
  row(VILLA, 'Beachfront villa', 'Pending', 18_500_000, '2026-09-02T09:00:00'),
  row(COTTAGE, 'Hilltop cottage', 'Construction', 9_000_000, '2026-08-15T10:30:00'),
  row(TOWER, 'City tower', 'Construction', 120_000_000, '2026-07-01T00:00:00'),
]

function signInAs(role: Role) {
  const claims = {
    sub: ADMIN_ID,
    name: 'Site Admin',
    email: 'admin@buildnexus.local',
    role,
    exp: Math.floor(Date.now() / 1000) + 3600,
  }

  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')

  localStorage.setItem(TOKEN_STORAGE_KEY, `header.${payload}.signature`)
}

type Call = { path: string; headers: Record<string, string> }

/**
 * Stands in for the API: the report route answers with `report` (or whatever
 * `reportFor` makes of the request), the export route with a small CSV — and
 * every call is recorded with its path and headers, which the shared
 * `stubFetch` does not keep.
 */
function stubApi(options: {
  report?: ProjectStatusReport
  reportFor?: (path: string) => Response
  exportResponse?: Response
}): Call[] {
  const calls: Call[] = []

  vi.stubGlobal('fetch', (path: string, init: RequestInit = {}) => {
    calls.push({ path, headers: (init.headers ?? {}) as Record<string, string> })

    if (path.includes('/export')) {
      return Promise.resolve(
        options.exportResponse ?? fileResponse(200, new Blob(['Status\r\n'], { type: 'text/csv' })),
      )
    }

    return Promise.resolve(
      options.reportFor ? options.reportFor(path) : apiResponse(200, options.report ?? reportOf([])),
    )
  })

  return calls
}

function renderPage() {
  signInAs('Admin')

  render(
    <MemoryRouter>
      <AuthProvider>
        <ProjectStatusReportPage />
      </AuthProvider>
    </MemoryRouter>,
  )
}

/**
 * The calls that are about the report. Not the export, and not the header's own request for a
 * Client's or Architect's unread notification count, which any page rendered in the shell makes.
 */
const reportCalls = (calls: Call[]) =>
  calls.filter(
    (call) => !call.path.includes('/export') && !call.path.startsWith('/api/projects/notifications'),
  )

/** The `download` name of every anchor the page clicked, in order. */
let savedAs: string[] = []

beforeEach(() => {
  URL.createObjectURL = vi.fn(() => 'blob:mock-url')
  URL.revokeObjectURL = vi.fn()

  // jsdom does not implement navigation, so a real click on the download link
  // only logs "not implemented". Record it instead — the file name is the point.
  savedAs = []
  vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
    this: HTMLAnchorElement,
  ) {
    savedAs.push(this.download)
  })
})

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
  localStorage.clear()
})

describe('ProjectStatusReportPage — grouped by status (AC-1)', () => {
  it('asks the service for the whole pipeline when it opens', async () => {
    const calls = stubApi({ report: reportOf(PIPELINE) })

    renderPage()

    await screen.findByTestId('group-Pending')
    expect(calls).toHaveLength(1)
    expect(calls[0].path).toBe('/api/projects/reports/status')
  })

  it('shows one group per status, in lifecycle order', async () => {
    stubApi({ report: reportOf(PIPELINE) })

    renderPage()

    await screen.findByTestId('group-Pending')
    const shown = STATUSES.map((status) => screen.getByTestId(`group-${status}`))

    // Document order, not just presence: the report reads down the lifecycle.
    for (let index = 1; index < shown.length; index++) {
      expect(
        shown[index - 1].compareDocumentPosition(shown[index]) & Node.DOCUMENT_POSITION_FOLLOWING,
      ).toBeTruthy()
    }
  })

  it('lists each project under its own status with a link to it', async () => {
    stubApi({ report: reportOf(PIPELINE) })

    renderPage()

    const pending = within(await screen.findByTestId('group-Pending'))
    expect(pending.getByRole('link', { name: 'Beachfront villa' })).toHaveAttribute(
      'href',
      `/projects/${VILLA}`,
    )
    expect(pending.queryByText('Hilltop cottage')).not.toBeInTheDocument()

    const construction = within(screen.getByTestId('group-Construction'))
    expect(construction.getByRole('link', { name: 'Hilltop cottage' })).toBeInTheDocument()
    expect(construction.getByRole('link', { name: 'City tower' })).toBeInTheDocument()
  })

  it('shows each group’s count and budget total', async () => {
    stubApi({ report: reportOf(PIPELINE) })

    renderPage()

    const construction = within(await screen.findByTestId('group-Construction'))
    expect(construction.getByText('2 projects')).toBeInTheDocument()
    expect(construction.getByText(/budget 129,000,000\.00/)).toBeInTheDocument()

    expect(within(screen.getByTestId('group-Pending')).getByText('1 project')).toBeInTheDocument()
  })

  it('shows the overall total above the groups', async () => {
    stubApi({ report: reportOf(PIPELINE) })

    renderPage()

    const summary = await screen.findByTestId('report-summary')
    expect(summary).toHaveTextContent('3 projects')
    expect(summary).toHaveTextContent('total budget 147,500,000.00')
    expect(summary).not.toHaveTextContent('filtered')
  })

  it('still shows a status with nothing in it, and says so', async () => {
    stubApi({ report: reportOf(PIPELINE) })

    renderPage()

    const completed = within(await screen.findByTestId('group-Completed'))
    expect(completed.getByText('0 projects')).toBeInTheDocument()
    expect(completed.getByText('No projects in this status.')).toBeInTheDocument()
  })

  it('shows the day a project was submitted as the day the filter would match it on', async () => {
    stubApi({ report: reportOf(PIPELINE) })

    renderPage()

    // The stored time carries no zone; the day is read straight off it rather
    // than through the browser's timezone.
    const villa = (await screen.findByRole('link', { name: 'Beachfront villa' })).closest('tr')!
    expect(within(villa).getByText('2026-09-02')).toBeInTheDocument()
  })

  it('says there are no projects when the pipeline is empty', async () => {
    stubApi({ report: reportOf([]) })

    renderPage()

    expect(await screen.findByText('There are no projects yet.')).toBeInTheDocument()
  })

  it('says it is loading until the report arrives', async () => {
    stubApi({ report: reportOf(PIPELINE) })

    renderPage()

    expect(screen.getByRole('status')).toHaveTextContent('Loading the report')
    await screen.findByTestId('group-Pending')
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('shows the service’s reason when the report cannot be loaded', async () => {
    stubApi({
      reportFor: () => apiResponse(500, { title: 'Server error', detail: 'The database is down.' }),
    })

    renderPage()

    expect(await screen.findByRole('alert')).toHaveTextContent('The database is down.')
    expect(screen.queryByTestId('group-Pending')).not.toBeInTheDocument()
  })
})

describe('ProjectStatusReportPage — a very large group', () => {
  /** `count` Pending projects, newest first, as the service returns them. */
  function manyPending(count: number) {
    return Array.from({ length: count }, (_, index) =>
      row(
        `00000000-0000-4000-8000-${String(index).padStart(12, '0')}`,
        `Project ${index + 1}`,
        'Pending',
        1_000_000,
        '2026-09-02T09:00:00',
      ),
    )
  }

  it('lists only the newest projects of a large group, and says how many there are', async () => {
    stubApi({ report: reportOf(manyPending(GROUP_PREVIEW_LIMIT + 30)) })

    renderPage()

    const pending = within(await screen.findByTestId('group-Pending'))
    expect(pending.getAllByRole('row')).toHaveLength(GROUP_PREVIEW_LIMIT + 1) // + header
    expect(pending.getByText(`${GROUP_PREVIEW_LIMIT + 30} projects`)).toBeInTheDocument()
    expect(pending.getByRole('link', { name: 'Project 1' })).toBeInTheDocument()
    expect(pending.queryByRole('link', { name: `Project ${GROUP_PREVIEW_LIMIT + 1}` })).not.toBeInTheDocument()
    expect(
      pending.getByText(new RegExp(`Showing the newest ${GROUP_PREVIEW_LIMIT} of ${GROUP_PREVIEW_LIMIT + 30}`)),
    ).toBeInTheDocument()
  })

  it('shows every project once Show all is pressed', async () => {
    const user = userEvent.setup()
    stubApi({ report: reportOf(manyPending(GROUP_PREVIEW_LIMIT + 30)) })

    renderPage()

    const pending = within(await screen.findByTestId('group-Pending'))
    await user.click(pending.getByRole('button', { name: `Show all ${GROUP_PREVIEW_LIMIT + 30}` }))

    expect(pending.getAllByRole('row')).toHaveLength(GROUP_PREVIEW_LIMIT + 30 + 1)
    expect(pending.getByRole('link', { name: `Project ${GROUP_PREVIEW_LIMIT + 30}` })).toBeInTheDocument()
    expect(pending.queryByRole('button', { name: /Show all/ })).not.toBeInTheDocument()
  })

  it('does not offer Show all for a group that fits', async () => {
    stubApi({ report: reportOf(manyPending(GROUP_PREVIEW_LIMIT)) })

    renderPage()

    const pending = within(await screen.findByTestId('group-Pending'))
    expect(pending.getAllByRole('row')).toHaveLength(GROUP_PREVIEW_LIMIT + 1)
    expect(pending.queryByRole('button', { name: /Show all/ })).not.toBeInTheDocument()
  })

  it('opens one group without opening another', async () => {
    const user = userEvent.setup()
    const cancelled = Array.from({ length: GROUP_PREVIEW_LIMIT + 5 }, (_, index) =>
      row(
        `11111111-0000-4000-8000-${String(index).padStart(12, '0')}`,
        `Cancelled ${index + 1}`,
        'Cancelled',
        1,
        '2026-09-01T09:00:00',
      ),
    )
    stubApi({ report: reportOf([...manyPending(GROUP_PREVIEW_LIMIT + 5), ...cancelled]) })

    renderPage()

    const pending = within(await screen.findByTestId('group-Pending'))
    await user.click(pending.getByRole('button', { name: /Show all/ }))

    expect(within(screen.getByTestId('group-Cancelled')).getByRole('button', { name: /Show all/ })).toBeInTheDocument()
  })

  it('counts the whole group in the totals, not just the rows shown', async () => {
    stubApi({ report: reportOf(manyPending(GROUP_PREVIEW_LIMIT + 30)) })

    renderPage()

    expect(await screen.findByTestId('report-summary')).toHaveTextContent(`${GROUP_PREVIEW_LIMIT + 30} projects`)
  })
})

describe('ProjectStatusReportPage — filtering (AC-2)', () => {
  it('narrows to the ticked statuses and asks the service for exactly those', async () => {
    const user = userEvent.setup()
    const calls = stubApi({
      reportFor: (path) =>
        apiResponse(
          200,
          path.includes('status=')
            ? reportOf(PIPELINE, ['Pending', 'Construction'])
            : reportOf(PIPELINE),
        ),
    })

    renderPage()
    await screen.findByTestId('group-Completed')

    await user.click(screen.getByRole('checkbox', { name: 'Pending' }))
    await user.click(screen.getByRole('checkbox', { name: 'Construction' }))
    await user.click(screen.getByRole('button', { name: 'Apply filters' }))

    await waitFor(() => expect(screen.queryByTestId('group-Completed')).not.toBeInTheDocument())
    expect(reportCalls(calls).at(-1)!.path).toBe(
      '/api/projects/reports/status?status=Pending&status=Construction',
    )
    expect(screen.getByTestId('group-Pending')).toBeInTheDocument()
    expect(screen.getByTestId('group-Construction')).toBeInTheDocument()
    expect(screen.getByTestId('report-summary')).toHaveTextContent('filtered')
  })

  it('does not ask again until the filters are applied', async () => {
    const user = userEvent.setup()
    const calls = stubApi({ report: reportOf(PIPELINE) })

    renderPage()
    await screen.findByTestId('group-Pending')

    await user.click(screen.getByRole('checkbox', { name: 'Pending' }))

    expect(reportCalls(calls)).toHaveLength(1)
  })

  it('narrows to a date range and asks the service for exactly that', async () => {
    const calls = stubApi({ report: reportOf(PIPELINE) })

    renderPage()
    await screen.findByTestId('group-Pending')

    fireEvent.change(screen.getByLabelText('Submitted from'), { target: { value: '2026-09-01' } })
    fireEvent.change(screen.getByLabelText('Submitted to'), { target: { value: '2026-09-30' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))

    await waitFor(() => expect(reportCalls(calls)).toHaveLength(2))
    expect(reportCalls(calls)[1].path).toBe(
      '/api/projects/reports/status?from=2026-09-01&to=2026-09-30',
    )
  })

  it('combines a status and a range', async () => {
    const user = userEvent.setup()
    const calls = stubApi({ report: reportOf(PIPELINE) })

    renderPage()
    await screen.findByTestId('group-Pending')

    await user.click(screen.getByRole('checkbox', { name: 'Design Approved' }))
    fireEvent.change(screen.getByLabelText('Submitted from'), { target: { value: '2026-09-01' } })
    await user.click(screen.getByRole('button', { name: 'Apply filters' }))

    await waitFor(() => expect(reportCalls(calls)).toHaveLength(2))
    expect(reportCalls(calls)[1].path).toBe(
      '/api/projects/reports/status?status=DesignApproved&from=2026-09-01',
    )
  })

  it('refuses a range that ends before it starts, beside the end date, without asking', async () => {
    const calls = stubApi({ report: reportOf(PIPELINE) })

    renderPage()
    await screen.findByTestId('group-Pending')

    fireEvent.change(screen.getByLabelText('Submitted from'), { target: { value: '2026-09-30' } })
    fireEvent.change(screen.getByLabelText('Submitted to'), { target: { value: '2026-09-01' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))

    expect(await screen.findByText('The end date must not be before the start date.')).toBeInTheDocument()
    expect(screen.getByLabelText('Submitted to')).toHaveAttribute('aria-invalid', 'true')
    expect(reportCalls(calls)).toHaveLength(1)
  })

  it('says so when nothing matches, rather than showing a report of empty groups as if it were the pipeline', async () => {
    stubApi({
      reportFor: (path) =>
        apiResponse(200, path.includes('from=') ? reportOf([]) : reportOf(PIPELINE)),
    })

    renderPage()
    await screen.findByTestId('group-Pending')

    fireEvent.change(screen.getByLabelText('Submitted from'), { target: { value: '2030-01-01' } })
    fireEvent.click(screen.getByRole('button', { name: 'Apply filters' }))

    expect(await screen.findByText('No projects match these filters.')).toBeInTheDocument()
    expect(screen.queryByText('There are no projects yet.')).not.toBeInTheDocument()
  })

  it('clears the filters and shows the whole pipeline again', async () => {
    const user = userEvent.setup()
    const calls = stubApi({ report: reportOf(PIPELINE) })

    renderPage()
    await screen.findByTestId('group-Pending')

    await user.click(screen.getByRole('checkbox', { name: 'Pending' }))
    await user.click(screen.getByRole('button', { name: 'Apply filters' }))
    await waitFor(() => expect(reportCalls(calls)).toHaveLength(2))

    await user.click(await screen.findByRole('button', { name: 'Clear' }))

    await waitFor(() => expect(reportCalls(calls)).toHaveLength(3))
    expect(reportCalls(calls)[2].path).toBe('/api/projects/reports/status')
    expect(screen.getByRole('checkbox', { name: 'Pending' })).not.toBeChecked()
  })

  it('shows the service’s reason when it refuses a filter', async () => {
    const user = userEvent.setup()
    stubApi({
      reportFor: (path) =>
        path.includes('status=')
          ? apiResponse(400, { title: 'Validation', detail: "'Finished' is not a project status." })
          : apiResponse(200, reportOf(PIPELINE)),
    })

    renderPage()
    await screen.findByTestId('group-Pending')

    await user.click(screen.getByRole('checkbox', { name: 'Pending' }))
    await user.click(screen.getByRole('button', { name: 'Apply filters' }))

    expect(await screen.findByRole('alert')).toHaveTextContent("'Finished' is not a project status.")
  })
})

describe('ProjectStatusReportPage — export (AC-2)', () => {
  it('downloads the CSV with the bearer token and saves it as a dated file', async () => {
    const user = userEvent.setup()
    const calls = stubApi({ report: reportOf(PIPELINE) })

    renderPage()
    await screen.findByTestId('group-Pending')

    await user.click(screen.getByRole('button', { name: 'Export CSV' }))

    await waitFor(() => expect(URL.createObjectURL).toHaveBeenCalledTimes(1))
    const exportCall = calls.find((call) => call.path.includes('/export'))!
    expect(exportCall.path).toBe('/api/projects/reports/status/export')
    expect(exportCall.headers.Authorization).toMatch(/^Bearer header\./)
    expect(savedAs).toHaveLength(1)
    expect(savedAs[0]).toMatch(/^project-status-report-\d{4}-\d{2}-\d{2}\.csv$/)
  })

  it('exports the filter that is applied, so what is saved is what is on screen', async () => {
    const user = userEvent.setup()
    const calls = stubApi({ report: reportOf(PIPELINE) })

    renderPage()
    await screen.findByTestId('group-Pending')

    await user.click(screen.getByRole('checkbox', { name: 'Construction' }))
    await user.click(screen.getByRole('button', { name: 'Apply filters' }))
    await waitFor(() => expect(reportCalls(calls)).toHaveLength(2))
    await screen.findByRole('button', { name: 'Export CSV' })

    await user.click(screen.getByRole('button', { name: 'Export CSV' }))

    await waitFor(() => expect(calls.some((call) => call.path.includes('/export'))).toBe(true))
    expect(calls.find((call) => call.path.includes('/export'))!.path).toBe(
      '/api/projects/reports/status/export?status=Construction',
    )
  })

  it('does not export a filter that has been ticked but not applied', async () => {
    const user = userEvent.setup()
    const calls = stubApi({ report: reportOf(PIPELINE) })

    renderPage()
    await screen.findByTestId('group-Pending')

    await user.click(screen.getByRole('checkbox', { name: 'Completed' }))
    await user.click(screen.getByRole('button', { name: 'Export CSV' }))

    await waitFor(() => expect(calls.some((call) => call.path.includes('/export'))).toBe(true))
    expect(calls.find((call) => call.path.includes('/export'))!.path).toBe(
      '/api/projects/reports/status/export',
    )
  })

  it('shows the service’s reason when the export is refused', async () => {
    const user = userEvent.setup()
    stubApi({
      report: reportOf(PIPELINE),
      exportResponse: apiResponse(400, { title: 'Validation', detail: 'That range is not valid.' }),
    })

    renderPage()
    await screen.findByTestId('group-Pending')

    await user.click(screen.getByRole('button', { name: 'Export CSV' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('That range is not valid.')
    expect(URL.createObjectURL).not.toHaveBeenCalled()
  })

  it('falls back to a plain message when the export fails without a reason', async () => {
    const user = userEvent.setup()
    stubApi({ report: reportOf(PIPELINE), exportResponse: apiResponse(500, {}) })

    renderPage()
    await screen.findByTestId('group-Pending')

    await user.click(screen.getByRole('button', { name: 'Export CSV' }))

    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })
})

describe('the project status report route — Admin only', () => {
  function openRoute(role: Role) {
    signInAs(role)
    window.history.pushState({}, '', '/admin/reports/project-status')

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
    stubApi({ report: reportOf(PIPELINE) })

    openRoute('Admin')

    expect(await screen.findByText('Project status report')).toBeInTheDocument()
    expect(await screen.findByTestId('group-Pending')).toBeInTheDocument()
  })

  it.each<Role>(['Client', 'Architect', 'ProjectManager'])(
    'tells a %s they do not have access, and never asks for the report',
    (role) => {
      const calls = stubApi({ report: reportOf(PIPELINE) })

      openRoute(role)

      expect(screen.getByText('You do not have access to this page')).toBeInTheDocument()
      expect(screen.queryByText('Project status report')).not.toBeInTheDocument()
      expect(reportCalls(calls)).toHaveLength(0)
    },
  )
})
