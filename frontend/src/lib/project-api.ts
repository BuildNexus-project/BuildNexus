import type { ApiFetchOptions } from './api'
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
  changedAt: string
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
 */
export function fetchProjects(authFetch: AuthFetch) {
  return authFetch<ProjectSummary[]>('/api/projects')
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
