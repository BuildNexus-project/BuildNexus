import type { ApiFetchOptions } from './api'

/** The token-attaching fetch handed out by the auth context. */
type AuthFetch = <T>(path: string, options?: Omit<ApiFetchOptions, 'token'>) => Promise<T>

/**
 * Where a project sits in its lifecycle, as the Project Service spells it.
 *
 * `Pending` is the only value so far, and deliberately the only one: US-05
 * creates a project awaiting review and says nothing about what happens next.
 * The story that adds approval or scheduling adds the value here too.
 */
export type ProjectStatus = 'Pending'

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
