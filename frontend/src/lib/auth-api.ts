import { apiFetch } from './api'
import type { Role, SelectableRole } from './roles'

export type UserProfile = {
  id: string
  fullName: string
  email: string
  role: Role
}

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
