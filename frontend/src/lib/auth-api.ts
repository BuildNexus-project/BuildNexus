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
 * One page of a longer list, with everything needed to page through the rest.
 *
 * `totalCount` is what makes a page usable: without it there is no telling a
 * last page from a full one. `totalPages` is the service's own arithmetic
 * rather than ours, and is at least 1 even when nothing matched.
 */
export type Page<T> = {
  items: T[]
  page: number
  pageSize: number
  /** Rows matching the filter, across every page — not just this one. */
  totalCount: number
  totalPages: number
}

/**
 * Which page of the Admin directory to ask for, and optionally which role to
 * narrow it to.
 *
 * Every field is optional: the service defaults to the first page of every
 * role, so an omitted query is a valid question rather than an error.
 */
export type AdminUserListQuery = {
  /** Absent lists every role. */
  role?: Role
  page?: number
  pageSize?: number
}

/**
 * The fields an administrator may change on somebody else's account —
 * the mirror image of {@link UpdateProfilePayload}.
 *
 * A user maintains their own name and contact details; the email, which is the
 * login identity, and the role, which is the authorisation boundary, are an
 * administrator's to move. Contact details are absent because they are the
 * user's own, and there is no password here — nobody sets another person's
 * password, which is what the reset link is for.
 *
 * All three are sent every time: the service replaces rather than patches, so
 * there is no "unset or unchanged?" ambiguity to resolve.
 */
export type AdminUpdateUserPayload = {
  fullName: string
  email: string
  role: Role
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

export type ForgotPasswordPayload = {
  email: string
}

export type ResetPasswordPayload = {
  /** Taken from the emailed link's query string, never typed by the user. */
  token: string
  newPassword: string
}

/** A response whose only content is a sentence to show the user. */
export type MessageResponse = {
  message: string
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
 * Asks the service to email a reset link to this address.
 *
 * Succeeds whether or not the address has an account: the service deliberately
 * answers the same way either way, so this cannot be used to find out who is
 * registered. Show the message it returns rather than one of our own.
 */
export function requestPasswordReset(payload: ForgotPasswordPayload) {
  return apiFetch<MessageResponse>('/api/auth/forgot-password', { method: 'POST', json: payload })
}

/**
 * Redeems an emailed reset link and sets the new password.
 *
 * Throws {@link ApiError} with status 400 when the link is unknown, expired or
 * already used — all three come back with the same message — or when the new
 * password fails the service's rules, which arrives as a `NewPassword` field
 * error.
 */
export function resetPassword(payload: ResetPasswordPayload) {
  return apiFetch<MessageResponse>('/api/auth/reset-password', { method: 'POST', json: payload })
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
 * Reads one page of the accounts on the platform, deactivated ones included.
 *
 * Admin only. Any other role gets {@link ApiError} with status 403 — the
 * service enforces that itself, whatever the router guard in front of this
 * page does.
 *
 * A page past the end is an empty page with the real total beside it, not a
 * 404. A page number below 1, a page size outside 1–100, or a role that is not
 * one of the four comes back as a 400.
 */
export function fetchAllUsers(authFetch: AuthFetch, query: AdminUserListQuery = {}) {
  // Only what was actually asked for goes on the URL; the service supplies the
  // rest, so its defaults are the single source of them.
  const params = new URLSearchParams()

  if (query.role) {
    params.set('role', query.role)
  }

  if (query.page !== undefined) {
    params.set('page', String(query.page))
  }

  if (query.pageSize !== undefined) {
    params.set('pageSize', String(query.pageSize))
  }

  const queryString = params.toString()

  return authFetch<Page<AdminUserSummary>>(`/api/users${queryString ? `?${queryString}` : ''}`)
}

/**
 * Saves an administrator's edit to somebody else's account and returns the row
 * as stored.
 *
 * Admin only. Throws {@link ApiError} with status 409 when the email already
 * belongs to another account, 400 with a per-field `errors` map when the
 * payload fails validation — which includes an administrator trying to change
 * their own role, since only an Admin can reach this and self-demotion is the
 * one edit nobody could undo — and 404 when the account has since been removed.
 */
export function updateUser(authFetch: AuthFetch, userId: string, payload: AdminUpdateUserPayload) {
  return authFetch<AdminUserSummary>(`/api/users/${userId}`, { method: 'PUT', json: payload })
}

/**
 * Withdraws or restores an account's access, returning the row as it now
 * stands.
 *
 * Admin only. Deactivating is what stops the holder signing in — login refuses
 * an inactive account, and so does a password reset, so a link cannot be used
 * to undo it. Nothing the account already authored is affected, and the same
 * call reinstates it, so this is not a one-way door.
 *
 * Throws {@link ApiError} with status 400 when an administrator aims it at
 * their own account: that refusal is what guarantees an active administrator
 * always remains.
 */
export function setUserActive(authFetch: AuthFetch, userId: string, isActive: boolean) {
  return authFetch<AdminUserSummary>(`/api/users/${userId}/status`, {
    method: 'PATCH',
    json: { isActive },
  })
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
