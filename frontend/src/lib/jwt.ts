import type { Role } from './roles'

/** The signed-in user, read from the access token's claims. */
export type AuthUser = {
  id: string
  fullName: string
  email: string
  role: Role
  /** When the token stops being accepted. */
  expiresAt: Date
}

type JwtPayload = {
  sub?: string
  name?: string
  email?: string
  role?: Role
  exp?: number
}

function decodeBase64Url(segment: string): string {
  const padded = segment.replace(/-/g, '+').replace(/_/g, '/')
  const binary = atob(padded.padEnd(padded.length + ((4 - (padded.length % 4)) % 4), '='))

  // atob yields bytes; re-decode them as UTF-8 so non-ASCII names survive.
  return decodeURIComponent(
    binary
      .split('')
      .map((character) => '%' + character.charCodeAt(0).toString(16).padStart(2, '0'))
      .join(''),
  )
}

/**
 * Reads the claims out of an access token.
 *
 * This is convenience only, never a security check — the signature is not
 * verified here and cannot be. The User Service validates every token it is
 * given; this just saves us storing a second copy of the user alongside it.
 *
 * Returns `null` for anything malformed, incomplete, or already expired.
 */
export function decodeToken(token: string): AuthUser | null {
  const segments = token.split('.')
  if (segments.length !== 3) {
    return null
  }

  let payload: JwtPayload
  try {
    payload = JSON.parse(decodeBase64Url(segments[1])) as JwtPayload
  } catch {
    return null
  }

  const { sub, name, email, role, exp } = payload
  if (!sub || !name || !email || !role || !exp) {
    return null
  }

  const expiresAt = new Date(exp * 1000)
  if (expiresAt.getTime() <= Date.now()) {
    return null
  }

  return { id: sub, fullName: name, email, role, expiresAt }
}
