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
 * Saves the signed-in user's profile and returns it as stored.
 *
 * Throws {@link ApiError} with status 400 and a per-field `errors` map when the
 * payload fails the service's validation.
 */
export function updateProfile(authFetch: AuthFetch, payload: UpdateProfilePayload) {
  return authFetch<UserProfile>('/api/users/me', { method: 'PUT', json: payload })
}
