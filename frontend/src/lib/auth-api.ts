import type { ApiFetchOptions } from './api'
import { apiFetch } from './api'
import type { Role, SelectableRole } from './roles'

export type UserProfile = {
  id: string
  fullName: string
  email: string
  /** `null` until the user fills it in. */
  phoneNumber: string | null
  /** `null` until the user fills it in. */
  contactAddress: string | null
  role: Role
}

/**
 * One row of the Admin user directory.
 *
 * Narrower than {@link UserProfile}: a roster needs who someone is, what they
 * may do and whether they can still sign in, not their contact details.
 */
export type AdminUserSummary = {
  id: string
  fullName: string
  email: string
  role: Role
  /** `false` for a deactivated account, which the directory still lists. */
  isActive: boolean
  /** ISO-8601, as the service serialises it. */
  createdAt: string
}

/**
 * One entry in the project-staff directory.
 *
 * Deliberately thinner than {@link AdminUserSummary}: a colleague's name and
 * what they do is all the service sends, so there is no email address or
 * account status here to be shown by mistake.
 */
export type DirectoryEntry = {
  id: string
  fullName: string
  role: Role
}

/**
 * The fields a user may change on their own account.
 *
 * Neither email nor role appears here: the User Service refuses a payload
 * carrying either with a 400, since the email is the login identity and the
 * role is the authorisation boundary. Both are an administrator's job.
 *
 * A blank phone number or address clears the stored value.
 */
export type UpdateProfilePayload = {
  fullName: string
  phoneNumber: string
  contactAddress: string
}

/** The token-attaching fetch handed out by the auth context. */
type AuthFetch = <T>(path: string, options?: Omit<ApiFetchOptions, 'token'>) => Promise<T>

export type RegisterPayload = {
  fullName: string
  email: string
  password: string
  role: SelectableRole
}

export type LoginPayload = {
  email: string
  password: string
}

export type AuthResponse = {
  tokenType: string
  accessToken: string
  expiresAtUtc: string
  user: UserProfile
}

/** Creates an account. Throws {@link ApiError} with status 409 if the email is taken. */
export function registerUser(payload: RegisterPayload) {
  return apiFetch<UserProfile>('/api/auth/register', { method: 'POST', json: payload })
}

/** Exchanges credentials for a signed token. Throws {@link ApiError} with 401 if they are wrong. */
export function loginUser(payload: LoginPayload) {
  return apiFetch<AuthResponse>('/api/auth/login', { method: 'POST', json: payload })
}

/**
 * Reads the signed-in user's profile.
 *
 * The access token already carries the name, email and role, but not the
 * contact details — and its copy stops being current the moment the profile is
 * edited. This is the authoritative version.
 */
export function fetchProfile(authFetch: AuthFetch) {
  return authFetch<UserProfile>('/api/users/me')
}

/**
 * Reads every account on the platform, deactivated ones included.
 *
 * Admin only. Any other role gets {@link ApiError} with status 403 — the
 * service enforces that itself, whatever the router guard in front of this
 * page does.
 */
export function fetchAllUsers(authFetch: AuthFetch) {
  return authFetch<AdminUserSummary[]>('/api/users')
}

/**
 * Lists the active Architects and Project Managers available to work on a
 * project.
 *
 * Architect and Project Manager only. A Client gets {@link ApiError} with
 * status 403, and so does an Admin — account administration is not project
 * work, and it reads {@link fetchAllUsers} instead.
 */
export function fetchProjectStaffDirectory(authFetch: AuthFetch) {
  return authFetch<DirectoryEntry[]>('/api/users/directory')
}

/**
 * Saves the signed-in user's profile and returns it as stored.
 *
 * Throws {@link ApiError} with status 400 and a per-field `errors` map when the
 * payload fails the service's validation.
 */
export function updateProfile(authFetch: AuthFetch, payload: UpdateProfilePayload) {
  return authFetch<UserProfile>('/api/users/me', { method: 'PUT', json: payload })
}
