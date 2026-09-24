import { ApiError, type ApiFetchOptions } from './api'

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
  /**
   * Where the build phase stands, or `null` when construction has not been started
   * (US-14 AC-4).
   *
   * Narrower than {@link ConstructionPhase}, which the Project Manager's own screen
   * reads: there is no `handedOverByUserId` here. That is a staff account id, of no
   * use to a Client and not theirs to see — the service omits it from this shape
   * rather than the page choosing not to render it.
   */
  phase: ConstructionPhaseSummary | null
}

/**
 * The build phase as a Client sees it — when their project started, finished and was
 * handed over to them (US-14 AC-4).
 */
export type ConstructionPhaseSummary = {
  status: ConstructionPhaseStatus
  /** ISO-8601. Never null — the phase exists because construction started. */
  startedAtUtc: string
  completedAtUtc: string | null
  handedOverAtUtc: string | null
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
/**
 * The three states a project's build phase moves through once it has formally
 * begun, exactly as the Construction Service spells them (US-14).
 *
 * There is no `NotStarted`: "construction has not started" is the *absence* of a
 * phase, which {@link fetchConstructionPhase} reports as `null` rather than as a
 * status. Giving it a name here would invite a phase object that claims the build
 * started while saying it has not.
 */
export const CONSTRUCTION_PHASE_STATUSES = ['Started', 'Completed', 'HandedOver'] as const

export type ConstructionPhaseStatus = (typeof CONSTRUCTION_PHASE_STATUSES)[number]

/** Display names — `HandedOver` reads badly in a UI. */
export const CONSTRUCTION_PHASE_STATUS_LABELS: Record<ConstructionPhaseStatus, string> = {
  Started: 'In Construction',
  Completed: 'Construction Complete',
  HandedOver: 'Handed Over',
}

/**
 * Where a project's build phase stands (US-14).
 *
 * The two nullable timestamps are how a caller tells the stages apart without
 * reading `status` twice: a phase with a `completedAtUtc` and no `handedOverAtUtc`
 * is a finished build awaiting handover. `startedAtUtc` is never null — the phase
 * exists because construction started.
 */
export type ConstructionPhase = {
  projectId: string
  status: ConstructionPhaseStatus
  /** ISO-8601, as the service serialises it. */
  startedAtUtc: string
  completedAtUtc: string | null
  handedOverAtUtc: string | null
  /**
   * The Project Manager who handed the project over; `null` until then. Handover
   * raises no event, so this is the only record of who ended the project — which is
   * why it is on the staff-facing shape and not on {@link ConstructionPhaseSummary}.
   */
  handedOverByUserId: string | null
  updatedAtUtc: string
}

/**
 * The precondition that refused a transition, from the 409's `reason` extension
 * (US-14 AC-3).
 *
 * The service sends one of these rather than only prose so a caller can act on
 * which gate failed. The two "already" cases are the ones worth branching on: they
 * mean the screen is looking at stale state and should re-read the phase, whereas
 * the rest mean the Project Manager has something to do first.
 */
export const CONSTRUCTION_REFUSAL_REASONS = [
  'DesignNotApproved',
  'NoMilestonesDefined',
  'AlreadyStarted',
  'NotStarted',
  'MilestonesIncomplete',
  'AlreadyCompleted',
] as const

export type ConstructionRefusalReason = (typeof CONSTRUCTION_REFUSAL_REASONS)[number]

/**
 * Whether a failed transition failed because the screen was out of date rather
 * than because the Project Manager has something left to do.
 *
 * `AlreadyStarted` and `AlreadyCompleted` both mean the transition the PM asked
 * for had already happened — someone else did it, or this tab has been open a
 * while. The right response is to re-read the phase and let the buttons settle,
 * not to ask them to fix anything.
 */
export function isStaleStateRefusal(error: unknown): boolean {
  if (!(error instanceof ApiError)) {
    return false
  }

  return error.reason === 'AlreadyStarted' || error.reason === 'AlreadyCompleted'
}

/**
 * Where the project's build phase stands, or `null` when construction has not
 * been started.
 *
 * Project-Manager only. The service answers 404 for a project whose build has not
 * begun; that is a real state rather than a failure — the screen reads it as
 * "Start construction is the next step" — so it is translated to `null` here and
 * the caller does not have to treat it as an error. Every other failure still
 * throws {@link ApiError}.
 */
export async function fetchConstructionPhase(
  authFetch: AuthFetch,
  projectId: string,
): Promise<ConstructionPhase | null> {
  try {
    return await authFetch<ConstructionPhase>(`/api/construction/projects/${projectId}/phase`)
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      return null
    }

    throw error
  }
}

/**
 * Formally starts construction on a project (US-14 AC-1).
 *
 * Project-Manager only, and only for a project whose design has been approved and
 * which has at least one milestone defined — both gates are checked by the service
 * inside the transaction that writes. A refused transition comes back as
 * {@link ApiError} with status 409, a `detail` written for the PM to read, and a
 * `reason` naming the precondition: `DesignNotApproved`, `NoMilestonesDefined` or
 * `AlreadyStarted`.
 *
 * On success the service publishes `ConstructionStarted`, which is what moves the
 * project's own status to Construction. That happens asynchronously, so a caller
 * that also shows the project's status should expect it to lag this reply.
 */
export function startConstruction(authFetch: AuthFetch, projectId: string) {
  return authFetch<ConstructionPhase>(`/api/construction/projects/${projectId}/start`, {
    method: 'POST',
  })
}

/**
 * Marks construction complete (US-14 AC-2).
 *
 * Project-Manager only, and only once construction has started *and* every
 * milestone on the project is `Completed` — gated independently of
 * {@link startConstruction}. A refused transition comes back as {@link ApiError}
 * with status 409 and a `reason` of `NotStarted`, `MilestonesIncomplete` or
 * `AlreadyCompleted`.
 *
 * On success the service publishes `ConstructionCompleted`.
 */
export function completeConstruction(authFetch: AuthFetch, projectId: string) {
  return authFetch<ConstructionPhase>(`/api/construction/projects/${projectId}/complete`, {
    method: 'POST',
  })
}

/**
 * Hands the finished project over to the Client, moving it to its terminal state
 * (US-14 AC-4).
 *
 * Project-Manager only, and only once construction is marked complete *and* the
 * project's final payment is recorded as settled — the two gates are checked
 * independently by the service inside the transaction that writes. A refused
 * transition comes back as {@link ApiError} with status 409 and a `reason` of
 * `NotStarted`, `NotCompleted`, `FinalPaymentNotSettled` or `AlreadyHandedOver`.
 *
 * `FinalPaymentNotSettled` is the one to expect in practice for now: the Payment
 * Service does not yet publish the settlement event this service listens for, so
 * until it does every handover is refused that way. The service fails closed
 * deliberately — a project held back can be handed over once the payment lands,
 * whereas one handed over unpaid cannot be un-handed.
 *
 * Publishes no event: handover is this service's own terminal state, and the
 * project already reached Completed on `ConstructionCompleted`.
 */
export function handOverConstruction(authFetch: AuthFetch, projectId: string) {
  return authFetch<ConstructionPhase>(`/api/construction/projects/${projectId}/handover`, {
    method: 'POST',
  })
}
