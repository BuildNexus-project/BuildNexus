import type { ApiFetchOptions } from './api'
import type { ProjectStatus } from './project-status'

/** The token-attaching fetch handed out by the auth context. */
type AuthFetch = <T>(path: string, options?: Omit<ApiFetchOptions, 'token'>) => Promise<T>

/** One project on the Admin's oversight screen. */
export type OversightProject = {
  id: string
  name: string
  location: string
  status: ProjectStatus
  /** `null` while nobody is on the project. */
  assignedArchitectId: string | null
  /** `null` while nobody is assigned, or when the name could not be found out. */
  assignedArchitectName: string | null
  assignedProjectManagerId: string | null
  assignedProjectManagerName: string | null
  /** ISO-8601, as the service serialises it. */
  createdAt: string
  /** ISO-8601. When the project last moved — the "last updated" date. */
  updatedAt: string
  /** The service's own verdict, so the rule has one home. */
  isStalled: boolean
}

/**
 * Every project on the platform (US-38).
 *
 * `stalledAfterDays` is the service's threshold, sent so this app quotes it
 * rather than keeping a second copy that could drift.
 */
export type ProjectOversight = {
  /** ISO-8601. The moment the stalled flags were judged at. */
  generatedAt: string
  stalledAfterDays: number
  totalProjects: number
  stalledCount: number
  /** Newest submitted first. Cancelled projects included. */
  projects: OversightProject[]
}

/**
 * Reads the platform oversight list.
 *
 * Admin only; any other role gets {@link ApiError} with status 403.
 */
export function fetchProjectOversight(authFetch: AuthFetch) {
  return authFetch<ProjectOversight>('/api/projects/oversight')
}

/**
 * How one project's staff slot reads on the screen.
 *
 * An id with no name is a person the User Service could not be asked about right
 * now (or no longer knows): the slot is filled, so it must not read as empty.
 */
export function staffLabel(name: string | null, id: string | null): string {
  if (id === null) {
    return 'Not assigned'
  }

  return name ?? 'Assigned (name unavailable)'
}

const DAY_MS = 24 * 60 * 60 * 1000

/** Whole days between a project's last update and `now`; never negative. */
export function daysSinceUpdate(updatedAt: string, now: Date = new Date()): number {
  return Math.max(0, Math.floor((now.getTime() - new Date(updatedAt).getTime()) / DAY_MS))
}
