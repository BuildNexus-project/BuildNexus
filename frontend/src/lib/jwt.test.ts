import { describe, expect, it } from 'vitest'

import { decodeToken } from './jwt'

function encodeBase64UrlRaw(text: string): string {
  const bytes = new TextEncoder().encode(text)
  let binary = ''
  bytes.forEach((byte) => {
    binary += String.fromCharCode(byte)
  })

  return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '')
}

function tokenWithPayload(payload: unknown): string {
  return `header.${encodeBase64UrlRaw(JSON.stringify(payload))}.signature`
}

const validClaims = {
  sub: '6f9619ff-8b86-d011-b42d-00cf4fc964ff',
  name: 'Ada Perera',
  email: 'ada@example.com',
  role: 'Client',
  exp: Math.floor(Date.now() / 1000) + 3600,
}

describe('decodeToken', () => {
  it('reads the claims out of a well-formed, unexpired token', () => {
    const user = decodeToken(tokenWithPayload(validClaims))

    expect(user).not.toBeNull()
    expect(user?.id).toBe(validClaims.sub)
    expect(user?.fullName).toBe(validClaims.name)
    expect(user?.email).toBe(validClaims.email)
    expect(user?.role).toBe('Client')
    expect(user?.expiresAt.getTime()).toBe(validClaims.exp * 1000)
  })

  it('decodes non-ASCII characters in the name correctly', () => {
    const user = decodeToken(tokenWithPayload({ ...validClaims, name: 'Amélie Dëz' }))

    expect(user?.fullName).toBe('Amélie Dëz')
  })

  it('returns null for a string that is not JWT-shaped', () => {
    expect(decodeToken('not-a-token')).toBeNull()
    expect(decodeToken('only.two')).toBeNull()
    expect(decodeToken('a.b.c.d')).toBeNull()
  })

  it('returns null when the payload segment is not valid base64url JSON', () => {
    expect(decodeToken(`header.${encodeBase64UrlRaw('not json at all')}.signature`)).toBeNull()
  })

  it.each(['sub', 'name', 'email', 'role', 'exp'])(
    'returns null when the %s claim is missing',
    (missingClaim) => {
      const { [missingClaim]: _omitted, ...incomplete } = validClaims as Record<string, unknown>

      expect(decodeToken(tokenWithPayload(incomplete))).toBeNull()
    },
  )

  it('returns null for a token that has already expired', () => {
    const expired = { ...validClaims, exp: Math.floor(Date.now() / 1000) - 60 }

    expect(decodeToken(tokenWithPayload(expired))).toBeNull()
  })

  it('is never used to decide access — the signature is never checked', () => {
    // decodeToken cannot verify a signature and does not try to: an unsigned
    // payload with the right shape decodes exactly like a real one. Every
    // caller of this is convenience-only, and the User Service revalidates.
    const unsigned = `header.${encodeBase64UrlRaw(JSON.stringify(validClaims))}.`

    expect(decodeToken(unsigned)).not.toBeNull()
  })
})
