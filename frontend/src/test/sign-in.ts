import type { Role } from '@/lib/roles'

const TOKEN_STORAGE_KEY = 'buildnexus.accessToken'

/** The account id every test user carries, so a page that reads `sub` has one to read. */
export const TEST_USER_ID = '6f9619ff-8b86-d011-b42d-00cf4fc964ff'

/**
 * Stores a token shaped like the one the User Service issues, minus a real signature — the
 * app never verifies one and cannot. Read back by `AuthProvider` on mount, so call this
 * before rendering.
 */
export function signInAs(role: Role, fullName = 'Ada Perera'): void {
  const claims = {
    sub: TEST_USER_ID,
    name: fullName,
    email: 'ada@example.com',
    role,
    exp: Math.floor(Date.now() / 1000) + 3600,
  }

  const payload = btoa(JSON.stringify(claims))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')

  localStorage.setItem(TOKEN_STORAGE_KEY, `header.${payload}.signature`)
}
