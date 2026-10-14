import { afterEach, describe, expect, it, vi } from 'vitest'

import { ApiError, apiFetch } from './api'
import {
  downloadProjectStatusReportCsv,
  fetchProjectStatusReport,
  projectReportFileName,
  projectReportQuery,
  type ProjectStatusReport,
} from './project-report-api'
import { EMPTY_PROJECT_REPORT_FILTER } from './project-report-schemas'
import { apiResponse, fileResponse, stubFetch } from '@/test/fake-fetch'

/**
 * The project status report client (US-18). These go through the real
 * `apiFetch`, so the request shape and the problem-details parsing are
 * exercised rather than mocked past.
 */
const authFetch = <T,>(path: string, options = {}) => apiFetch<T>(path, options)

const report: ProjectStatusReport = {
  generatedAt: '2026-09-30T08:00:00Z',
  totalProjects: 2,
  totalBudget: 3_000_000,
  groups: [
    {
      status: 'Pending',
      count: 2,
      totalBudget: 3_000_000,
      projects: [
        {
          id: '11111111-2222-4333-8444-555555555555',
          name: 'Beachfront villa',
          location: 'Galle',
          status: 'Pending',
          budget: 2_000_000,
          createdAt: '2026-09-02T09:00:00Z',
          updatedAt: '2026-09-02T09:00:00Z',
        },
      ],
    },
  ],
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('projectReportQuery', () => {
  it('is empty when nothing is being filtered', () => {
    expect(projectReportQuery(EMPTY_PROJECT_REPORT_FILTER)).toBe('')
  })

  it('repeats the status parameter once per status', () => {
    expect(projectReportQuery({ ...EMPTY_PROJECT_REPORT_FILTER, statuses: ['Pending', 'Designing'] })).toBe(
      '?status=Pending&status=Designing',
    )
  })

  it('sends the range as from and to', () => {
    expect(
      projectReportQuery({ statuses: [], from: '2026-09-01', to: '2026-09-30' }),
    ).toBe('?from=2026-09-01&to=2026-09-30')
  })

  it('leaves a blank end of the range out rather than sending it empty', () => {
    expect(projectReportQuery({ statuses: [], from: '2026-09-01', to: '' })).toBe('?from=2026-09-01')
    expect(projectReportQuery({ statuses: [], from: '', to: '2026-09-30' })).toBe('?to=2026-09-30')
  })

  it('combines status and range', () => {
    expect(
      projectReportQuery({ statuses: ['Construction'], from: '2026-09-01', to: '2026-09-30' }),
    ).toBe('?status=Construction&from=2026-09-01&to=2026-09-30')
  })
})

describe('fetchProjectStatusReport', () => {
  it('reads the report from the reports route with a plain GET', async () => {
    const requests = stubFetch(apiResponse(200, report))

    const result = await fetchProjectStatusReport(authFetch, EMPTY_PROJECT_REPORT_FILTER)

    expect(requests[0].path).toBe('/api/projects/reports/status')
    expect(requests[0].method).toBeUndefined()
    expect(result).toEqual(report)
  })

  it('sends the filter as the query string', async () => {
    const requests = stubFetch(apiResponse(200, report))

    await fetchProjectStatusReport(authFetch, {
      statuses: ['Designing'],
      from: '2026-09-01',
      to: '2026-09-30',
    })

    expect(requests[0].path).toBe(
      '/api/projects/reports/status?status=Designing&from=2026-09-01&to=2026-09-30',
    )
  })

  it('surfaces a refused filter as an ApiError carrying the reason', async () => {
    stubFetch(
      apiResponse(400, {
        title: 'One or more validation errors occurred.',
        errors: { filter: ["'Finished' is not a project status."] },
      }),
    )

    const error = await fetchProjectStatusReport(authFetch, EMPTY_PROJECT_REPORT_FILTER).catch(
      (caught: unknown) => caught,
    )

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(400)
    expect((error as ApiError).fieldErrors.filter).toEqual(["'Finished' is not a project status."])
  })

  it('surfaces a non-Admin caller as a 403', async () => {
    stubFetch(apiResponse(403, { title: 'Forbidden' }))

    const error = await fetchProjectStatusReport(authFetch, EMPTY_PROJECT_REPORT_FILTER).catch(
      (caught: unknown) => caught,
    )

    expect((error as ApiError).status).toBe(403)
  })
})

describe('downloadProjectStatusReportCsv', () => {
  it('asks the export route with the same filter and the bearer token', async () => {
    const calls: Array<{ path: string; headers: unknown }> = []
    vi.stubGlobal('fetch', (path: string, init: RequestInit = {}) => {
      calls.push({ path, headers: init.headers })
      return Promise.resolve(fileResponse(200, new Blob(['Status\r\n'], { type: 'text/csv' })))
    })

    const blob = await downloadProjectStatusReportCsv('token-123', {
      statuses: ['Pending'],
      from: '2026-09-01',
      to: '',
    })

    expect(calls[0].path).toBe('/api/projects/reports/status/export?status=Pending&from=2026-09-01')
    expect(calls[0].headers).toEqual({ Authorization: 'Bearer token-123' })
    expect(await blob.text()).toBe('Status\r\n')
  })

  it('sends no Authorization header when there is no token', async () => {
    const calls: Array<{ headers: unknown }> = []
    vi.stubGlobal('fetch', (_path: string, init: RequestInit = {}) => {
      calls.push({ headers: init.headers })
      return Promise.resolve(fileResponse(200, new Blob([''])))
    })

    await downloadProjectStatusReportCsv(null, EMPTY_PROJECT_REPORT_FILTER)

    expect(calls[0].headers).toEqual({})
  })

  it('throws an ApiError with the service’s reason for a refused filter', async () => {
    stubFetch(
      apiResponse(400, {
        title: 'One or more validation errors occurred.',
        errors: { filter: ['The from date must not be after the to date.'] },
      }),
    )

    const error = await downloadProjectStatusReportCsv('t', EMPTY_PROJECT_REPORT_FILTER).catch(
      (caught: unknown) => caught,
    )

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(400)
    expect((error as ApiError).fieldErrors.filter).toEqual([
      'The from date must not be after the to date.',
    ])
  })

  it('throws an ApiError for a 403 whose body is not JSON', async () => {
    vi.stubGlobal('fetch', () =>
      Promise.resolve({ ok: false, status: 403, text: () => Promise.resolve('<html>') } as Response),
    )

    const error = await downloadProjectStatusReportCsv('t', EMPTY_PROJECT_REPORT_FILTER).catch(
      (caught: unknown) => caught,
    )

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(403)
  })
})

describe('projectReportFileName', () => {
  it('is dated by the day of the export', () => {
    expect(projectReportFileName(new Date('2026-09-30T23:30:00Z'))).toBe(
      'project-status-report-2026-09-30.csv',
    )
  })
})
