import type { ApiFetchOptions } from './api'
import type { ProjectEventType } from './project-events'
import type { ProjectStatus } from './project-status'
import type { Role } from './roles'

/** The token-attaching fetch handed out by the auth context. */
type AuthFetch = <T>(path: string, options?: Omit<ApiFetchOptions, 'token'>) => Promise<T>

/** A project as the service returns it. */
export type Project = {
  id: string
  /** The Client it belongs to, taken from the token rather than the payload. */
  clientId: string
  name: string
  location: string
  landSizePerches: number
  budget: number
  floors: number
  bedrooms: number
  bathrooms: number
  garageSpaces: number
  /** `null` when the Client had nothing to add. */
  otherRequirements: string | null
  status: ProjectStatus
  /** ISO-8601, as the service serialises it. */
  createdAt: string
}

/**
 * The requirements a Client submits for a new project.
 *
 * There is no client id here: the service reads it from the `sub` claim of the
 * caller's own token, so a project cannot be submitted on somebody else's
 * behalf. Neither is there a status — a submitted project is `Pending`, and the
 * caller has no say in that.
 *
 * A blank `otherRequirements` is stored as nothing.
 */
export type CreateProjectPayload = {
  name: string
  location: string
  landSizePerches: number
  budget: number
  floors: number
  bedrooms: number
  bathrooms: number
  garageSpaces: number
  otherRequirements: string
}

/** One row of the project list — enough to recognise a project and pick it. */
export type ProjectSummary = {
  id: string
  clientId: string
  name: string
  location: string
  status: ProjectStatus
  /** ISO-8601, as the service serialises it. */
  createdAt: string
  /** When it last moved, which is what says whether it is being worked on. */
  updatedAt: string
}

/**
 * One entry in a project's status history.
 *
 * `fromStatus` is `null` on the opening entry only — the project did not come
 * from anywhere, it was created.
 */
export type ProjectStatusChange = {
  id: string
  fromStatus: ProjectStatus | null
  toStatus: ProjectStatus
  /**
   * Who made the change, as an id rather than a name: the account lives in the
   * User Service's own database and the Project Service holds no copy of it.
   */
  changedByUserId: string
  /** The role they held at the time, not the role they hold now. */
  changedByRole: Role
  /**
   * Why the change was made, when there is a reason on record. `null` for a
   * move that speaks for itself; a cancellation records its reason here.
   */
  note: string | null
  changedAt: string
}

/**
 * One event the Project Service raised for a project, and how its delivery to
 * the message bus went.
 *
 * The publish no longer happens inside the request that caused it — the event
 * is written into the service's outbox in the same transaction as the change,
 * and sent afterwards — which is what makes it survive the broker being down.
 * It also means "did the other services get told?" is a question only this can
 * answer.
 */
export type ProjectEvent = {
  /**
   * The event's own id, and the `eventId` carried in the envelope on the topic
   * — so a message a consumer asks about can be matched to this row.
   */
  id: string
  /** `ProjectCreated`, `ProjectUpdated` or `ProjectApproved`. */
  eventType: ProjectEventType
  /** When the change it announces happened, not when it was sent. */
  occurredAt: string
  /** When the broker took it, or `null` while it is still waiting. */
  publishedAt: string | null
  /**
   * How many times delivery has been tried. More than one on a delivered event
   * means it got there eventually, which is the outbox working rather than a
   * fault.
   */
  attemptCount: number
  /**
   * Why the last attempt failed, or `null`. Cleared once the event is
   * delivered, so a value here alongside a null `publishedAt` is the thing
   * worth looking at.
   */
  lastError: string | null
}

/**
 * A project in full: its requirements, its status, who is on it, when it was
 * created, and every status change it has been through.
 */
export type ProjectDetail = Project & {
  /** `null` while nobody is assigned, which is every project so far. */
  assignedArchitectId: string | null
  assignedProjectManagerId: string | null
  updatedAt: string
  /** Oldest first, starting at the project's creation. */
  statusHistory: ProjectStatusChange[]
  /**
   * What the project may move to next — one status, or none once it is
   * Completed.
   *
   * The service's answer, so the UI offers exactly the moves that will be
   * accepted rather than keeping its own copy of the transition table. It says
   * what the *project* may do, not what this user may do: the role decides
   * whether a button is offered at all.
   */
  allowedNextStatuses: ProjectStatus[]
}

/**
 * Submits a new construction project and returns it as stored.
 *
 * Client only. Any other role gets {@link ApiError} with status 403 — the
 * service enforces that itself, whatever the router guard in front of the page
 * does. A payload that fails the service's rules comes back as a 400 with a
 * per-field `errors` map.
 */
export function createProject(authFetch: AuthFetch, payload: CreateProjectPayload) {
  return authFetch<Project>('/api/projects', { method: 'POST', json: payload })
}

/**
 * The projects the signed-in user may see, newest first.
 *
 * Scoped by the service to the caller's own involvement: a Client gets the
 * projects they submitted, an Architect or Project Manager the ones they are
 * assigned to, and an Admin all of them. Somebody with none gets an empty list
 * rather than a refusal.
 *
 * Cancelled projects are left out unless `includeCancelled` is set — a
 * closed-out project is not being worked on, but stays reachable through
 * {@link fetchProject}.
 */
export function fetchProjects(authFetch: AuthFetch, { includeCancelled = false } = {}) {
  return authFetch<ProjectSummary[]>(
    includeCancelled ? '/api/projects?includeCancelled=true' : '/api/projects',
  )
}

/**
 * One project in full, with its status history.
 *
 * A caller who is not the owning client, assigned staff or an Admin gets
 * {@link ApiError} with status 403, and an id that does not exist gets a 404.
 */
export function fetchProject(authFetch: AuthFetch, projectId: string) {
  return authFetch<ProjectDetail>(`/api/projects/${projectId}`)
}

/**
 * The events raised for one project, oldest first.
 *
 * Admin only: this is the integration answering for itself — delivery state,
 * attempt counts and broker error text — not project information. Any other
 * role gets {@link ApiError} with status 403, and the service enforces that
 * whatever this app chooses to render.
 *
 * An empty list is a real answer, not a failure: a project created before the
 * outbox existed has raised nothing. An id that does not exist is a 404.
 */
export function fetchProjectEvents(authFetch: AuthFetch, projectId: string) {
  return authFetch<ProjectEvent[]>(`/api/projects/${projectId}/events`)
}

/**
 * Moves a project to its next status and returns it as it now stands, with the
 * new entry already in its history.
 *
 * Architect, Project Manager and Admin only, and only on a project they are
 * actually on — the owning Client can watch every step but does not declare
 * their own design approved. A move the lifecycle does not allow comes back as
 * a 400 whose `detail` says where the project actually is, and a 409 means
 * somebody else moved it first.
 */
export function updateProjectStatus(
  authFetch: AuthFetch,
  projectId: string,
  status: ProjectStatus,
) {
  return authFetch<ProjectDetail>(`/api/projects/${projectId}/status`, {
    method: 'PATCH',
    json: { status },
  })
}

/**
 * Puts an Architect on a project and returns it as it now stands.
 *
 * Admin only. Assigning an Architect to a project that is still `Pending` also
 * moves it to `Designing` — the returned {@link ProjectDetail} carries the new
 * status and history entry. A project already past `Pending` just has the slot
 * set or replaced.
 *
 * The account must hold the Architect role: an unknown id or the wrong role
 * comes back as {@link ApiError} with status 400 and an `ArchitectId` field
 * error, an id that is not a project as a 404, a 409 means somebody moved the
 * project off `Pending` first, and a 502 means the User Service could not be
 * reached to check the role.
 */
export function assignArchitect(authFetch: AuthFetch, projectId: string, architectId: string) {
  return authFetch<ProjectDetail>(`/api/projects/${projectId}/architect`, {
    method: 'PUT',
    json: { architectId },
  })
}

/**
 * Puts a Project Manager on a project and returns it as it now stands.
 *
 * Admin only, and the mirror of {@link assignArchitect} for the PM slot —
 * except there is no status change: a PM is put on a project without moving it.
 * A `ProjectManagerId` field error means an unknown id or an account that is
 * not a Project Manager.
 */
export function assignProjectManager(
  authFetch: AuthFetch,
  projectId: string,
  projectManagerId: string,
) {
  return authFetch<ProjectDetail>(`/api/projects/${projectId}/project-manager`, {
    method: 'PUT',
    json: { projectManagerId },
  })
}

/**
 * Cancels a project before construction starts and returns it as it now
 * stands — Cancelled, with the reason in its history.
 *
 * The owning Client or an Admin only (US-08). A project in Construction,
 * Completed, or already Cancelled comes back as {@link ApiError} with status
 * 400 whose `detail` says why; anyone else gets a 403; a 409 means somebody
 * moved the project on first. The reason is required — a blank one is a 400
 * with a `Reason` field error.
 */
export function cancelProject(authFetch: AuthFetch, projectId: string, reason: string) {
  return authFetch<ProjectDetail>(`/api/projects/${projectId}/cancellation`, {
    method: 'POST',
    json: { reason },
  })
}
