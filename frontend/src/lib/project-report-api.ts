import type { ApiFetchOptions } from './api'
import { ApiError } from './api'
import type { ProjectReportFilterValues } from './project-report-schemas'
import type { ProjectStatus } from './project-status'

/** The token-attaching fetch handed out by the auth context. */
type AuthFetch = <T>(path: string, options?: Omit<ApiFetchOptions, 'token'>) => Promise<T>

/**
 * One project inside a group of the status report — enough to recognise it and
 * follow the link to it.
 */
export type ProjectReportRow = {
  id: string
  name: string
  location: string
  status: ProjectStatus
  budget: number
  /** ISO-8601, as the service serialises it. The day the project was submitted. */
  createdAt: string
  /** ISO-8601. When the project last moved. */
  updatedAt: string
}

/** The projects of one status, with the count and budget total the pipeline is read by. */
export type ProjectStatusGroup = {
  status: ProjectStatus
  count: number
  totalBudget: number
  /** Newest submitted first. Empty when nothing is in this status. */
  projects: ProjectReportRow[]
}

/**
 * The project status report (US-18): every project grouped by current status.
 *
 * `groups` is one entry per status in scope, in lifecycle order, and includes
 * statuses nothing is in — so the report keeps the same shape from one week to
 * the next.
 */
export type ProjectStatusReport = {
  /** ISO-8601. When the service produced it — the moment the pipeline is described at. */
  generatedAt: string
  totalProjects: number
  totalBudget: number
  groups: ProjectStatusGroup[]
}

const REPORT_PATH = '/api/projects/reports/status'

/**
 * The query string for a filter, with a leading `?`, or an empty string when
 * nothing is being filtered.
 *
 * Statuses are repeated (`status=Pending&status=Designing`), which is what the
 * service binds; a blank day is left out rather than sent empty, since an empty
 * value is not "no limit" to a date parameter but a malformed date.
 */
export function projectReportQuery(filter: ProjectReportFilterValues): string {
  const params = new URLSearchParams()

  for (const status of filter.statuses) {
    params.append('status', status)
  }

  if (filter.from !== '') {
    params.set('from', filter.from)
  }

  if (filter.to !== '') {
    params.set('to', filter.to)
  }

  const query = params.toString()

  return query === '' ? '' : `?${query}`
}

/**
 * Reads the project status report.
 *
 * Admin only; any other role gets {@link ApiError} with status 403. A filter the
 * service cannot make sense of — an unknown status, a range that ends before it
 * starts — is a 400 whose `fieldErrors.filter` says why.
 */
export function fetchProjectStatusReport(authFetch: AuthFetch, filter: ProjectReportFilterValues) {
  return authFetch<ProjectStatusReport>(`${REPORT_PATH}${projectReportQuery(filter)}`)
}

/**
 * Downloads the report as CSV, with the same filter the screen is showing.
 *
 * Goes straight through `fetch` rather than the JSON client: the body is the
 * file's bytes, not JSON. Throws {@link ApiError} for a non-2xx response —
 * a 403 for anyone who is not an Admin, a 400 for a filter the service refuses.
 */
export async function downloadProjectStatusReportCsv(
  token: string | null,
  filter: ProjectReportFilterValues,
): Promise<Blob> {
  const response = await fetch(`${REPORT_PATH}/export${projectReportQuery(filter)}`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  })

  if (!response.ok) {
    const raw = await response.text()

    let problem: { title?: string; detail?: string; errors?: Record<string, string[]> } = {}
    try {
      problem = raw ? (JSON.parse(raw) as typeof problem) : {}
    } catch {
      // A non-JSON error body has nothing to show; the status still stands.
    }

    throw new ApiError(response.status, {
      title: problem.title,
      detail: problem.detail,
      fieldErrors: problem.errors,
    })
  }

  return response.blob()
}

/**
 * The name the saved file gets, dated so exports from different days sit side
 * by side. Matches the name the service suggests, which the browser cannot read
 * from a cross-origin response.
 */
export function projectReportFileName(day: Date = new Date()): string {
  return `project-status-report-${day.toISOString().slice(0, 10)}.csv`
}
