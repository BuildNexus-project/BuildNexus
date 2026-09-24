import type { ApiFetchOptions } from './api'

/** The token-attaching fetch handed out by the auth context. */
type AuthFetch = <T>(path: string, options?: Omit<ApiFetchOptions, 'token'>) => Promise<T>

/**
 * The three states a milestone moves through, exactly as the Construction
 * Service spells them (US-12 AC-2). The wire format is the string name — a
 * global `JsonStringEnumConverter` on the service side keeps request and
 * response in the same shape.
 */
export const MILESTONE_STATUSES = ['NotStarted', 'InProgress', 'Completed'] as const

export type MilestoneStatus = (typeof MILESTONE_STATUSES)[number]

/**
 * Display names — the wire values read badly in a UI. `NotStarted` becomes
 * `Not Started`, `InProgress` becomes `In Progress`.
 */
export const MILESTONE_STATUS_LABELS: Record<MilestoneStatus, string> = {
  NotStarted: 'Not Started',
  InProgress: 'In Progress',
  Completed: 'Completed',
}

/**
 * One construction milestone a Project Manager defined for an approved
 * project.
 *
 * `updatedAtUtc` moves on every status change and is what a "recently changed"
 * table can sort by; `createdAtUtc` never moves after the row is written.
 */
export type Milestone = {
  id: string
  projectId: string
  name: string
  status: MilestoneStatus
  /** ISO-8601, as the service serialises it. */
  createdAtUtc: string
  updatedAtUtc: string
}

/**
 * A project's aggregated milestone progress (US-12 AC-3).
 *
 * `progressPercent` is computed by the service on every read from
 * `completedMilestones / totalMilestones × 100`, rounded to two decimals.
 * Nothing stores it, so a response cannot disagree with the milestones behind
 * it. Zero when the project has no milestones yet — a real state, not a
 * failure.
 */
export type ProjectProgress = {
  projectId: string
  totalMilestones: number
  completedMilestones: number
  progressPercent: number
}

/**
 * Everything a Client's dashboard needs to show how their project is
 * advancing (US-13): the per-milestone status, and the rollup the progress bar
 * is drawn from.
 *
 * The rollup fields sit at the top level, mirroring {@link ProjectProgress},
 * rather than nesting it — the dashboard reads `progressPercent` the same way
 * the Project Manager's view does. The service recomputes the percentage on
 * every read, so it cannot disagree with the `milestones` beside it.
 */
export type ProjectProgressSummary = {
  projectId: string
  totalMilestones: number
  completedMilestones: number
  progressPercent: number
  /** Oldest first — the order the PM planned them in, which is the order the build runs in. */
  milestones: Milestone[]
}

/**
 * How the signed-in Client's project is advancing (US-13 AC-1).
 *
 * Client-only, and only for a project the caller owns — the service checks its
 * own record of who owns the project and answers {@link ApiError} with status
 * 403 otherwise, whether the project belongs to someone else, has no recorded
 * owner, or does not exist. A project whose design has not yet been approved
 * comes back as 403's counterpart, a 404: there is no construction plan to
 * report on. A project that is approved but has no milestones yet is a real
 * answer with an empty list at `0`, not an error.
 *
 * One request rather than the two the PM's screen makes: the dashboard re-reads
 * this on a timer, so two calls per refresh would double the traffic and open a
 * window where the list and the percentage disagree.
 */
export function fetchProjectProgressSummary(authFetch: AuthFetch, projectId: string) {
  return authFetch<ProjectProgressSummary>(
    `/api/construction/projects/${projectId}/progress-summary`,
  )
}

/**
 * What a Project Manager submits to add a milestone.
 *
 * There is no project id here: it is in the URL. The initial status is always
 * `NotStarted` — the service decides that.
 */
export type CreateMilestonePayload = {
  name: string
}

/**
 * Adds a milestone to an approved project.
 *
 * Project-Manager only, and only for a project whose design has already been
 * approved (US-12 AC-1) — the service gate checks the local
 * `milestone_setups` row the `DesignApproved` consumer plants. A project
 * without one comes back as {@link ApiError} with status 400 whose `detail`
 * says the design has not yet been approved; a repeated milestone name for
 * the same project comes back as 409; any other role gets 403.
 */
export function createMilestone(
  authFetch: AuthFetch,
  projectId: string,
  payload: CreateMilestonePayload,
) {
  return authFetch<Milestone>(`/api/construction/projects/${projectId}/milestones`, {
    method: 'POST',
    json: payload,
  })
}

/**
 * The milestones defined for a project, oldest first — the order the PM
 * planned them in.
 *
 * Project-Manager only. Empty is a real answer for a project with no
 * milestones defined yet, whether the design was only just approved or nobody
 * has planned any. Callers who need to distinguish "not approved" from "none
 * yet" read {@link fetchProjectProgress}, which gates on the design-approval
 * marker.
 */
export function fetchProjectMilestones(authFetch: AuthFetch, projectId: string) {
  return authFetch<Milestone[]>(`/api/construction/projects/${projectId}/milestones`)
}

/**
 * Moves a milestone to a new status (US-12 AC-2).
 *
 * Project-Manager only. An unknown milestone id comes back as
 * {@link ApiError} with status 404. The three-state domain is enforced by the
 * service — anything outside `NotStarted`, `InProgress` or `Completed` is
 * refused at the JSON boundary before the endpoint runs.
 */
export function updateMilestoneStatus(
  authFetch: AuthFetch,
  milestoneId: string,
  status: MilestoneStatus,
) {
  return authFetch<Milestone>(`/api/construction/milestones/${milestoneId}/status`, {
    method: 'PATCH',
    json: { status },
  })
}

/**
 * The project's aggregated milestone progress (US-12 AC-3), recomputed by the
 * service on every read.
 *
 * Project-Manager only. A project whose design has not yet been approved
 * comes back as {@link ApiError} with status 404 — no plan exists, so no
 * percentage is meaningful. A project that is approved but has no milestones
 * yet is a real answer with `0` / `0` / `0`, not an error.
 */
export function fetchProjectProgress(authFetch: AuthFetch, projectId: string) {
  return authFetch<ProjectProgress>(`/api/construction/projects/${projectId}/progress`)
}

/**
 * Plants the seven canonical construction milestones — Foundation, Walls,
 * Roof, Electrical, Plumbing, Painting, Finishing — on an approved project
 * in one call (US-12 Definition of Done).
 *
 * Project-Manager only. Idempotent on the service: names already on the
 * project are silently skipped, so a repeat click or a template applied on
 * top of a couple of hand-typed milestones is fine. Returns the union of
 * previously-existing and newly-inserted rows for the template names, in
 * canonical order. A project whose design has not yet been approved comes
 * back as {@link ApiError} with status 400.
 */
export function createMilestonesFromTemplate(authFetch: AuthFetch, projectId: string) {
  return authFetch<Milestone[]>(
    `/api/construction/projects/${projectId}/milestones/from-template`,
    { method: 'POST' },
  )
}