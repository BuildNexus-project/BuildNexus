import { describe, expect, it } from 'vitest'

import {
  adminUserSchema,
  forgotPasswordSchema,
  loginSchema,
  profileSchema,
  registerSchema,
  resetPasswordSchema,
} from './auth-schemas'

describe('registerSchema', () => {
  const valid = {
    fullName: 'Ada Perera',
    email: 'ada@example.com',
    password: 'Passw0rd',
    role: 'Client',
  }

  it('accepts a well-formed registration', () => {
    expect(registerSchema.safeParse(valid).success).toBe(true)
  })

  it('refuses a password shorter than 8 characters', () => {
    const result = registerSchema.safeParse({ ...valid, password: 'Pw0rd1' })

    expect(result.success).toBe(false)
  })

  it('refuses a password with no digit', () => {
    const result = registerSchema.safeParse({ ...valid, password: 'NoDigitsHere' })

    expect(result.success).toBe(false)
  })

  it('refuses a password with no letter', () => {
    const result = registerSchema.safeParse({ ...valid, password: '12345678' })

    expect(result.success).toBe(false)
  })

  it('refuses a full name shorter than 2 characters', () => {
    expect(registerSchema.safeParse({ ...valid, fullName: 'A' }).success).toBe(false)
  })

  it('refuses a malformed email', () => {
    expect(registerSchema.safeParse({ ...valid, email: 'not-an-email' }).success).toBe(false)
  })

  it('refuses Admin — self-registration cannot mint one', () => {
    // Mirrors the User Service, which refuses a self-assigned Admin role with a
    // 400. SELECTABLE_ROLES is what keeps this schema from ever accepting it.
    expect(registerSchema.safeParse({ ...valid, role: 'Admin' }).success).toBe(false)
  })
})

describe('loginSchema', () => {
  it('accepts an email and a non-empty password', () => {
    expect(loginSchema.safeParse({ email: 'ada@example.com', password: 'x' }).success).toBe(true)
  })

  it('accepts a password registerSchema would refuse — presence is the only rule here', () => {
    // Deliberate: applying the registration rules on login would let a caller
    // tell a badly-formed password apart from a wrong one. "short" is under 8
    // characters and has no digit — registerSchema refuses it, login does not.
    expect(registerSchema.safeParse({
      fullName: 'Ada Perera', email: 'ada@example.com', password: 'short', role: 'Client',
    }).success).toBe(false)
    expect(loginSchema.safeParse({ email: 'ada@example.com', password: 'short' }).success).toBe(true)
  })

  it('refuses an empty password', () => {
    expect(loginSchema.safeParse({ email: 'ada@example.com', password: '' }).success).toBe(false)
  })

  it('refuses a malformed email', () => {
    expect(loginSchema.safeParse({ email: 'not-an-email', password: 'x' }).success).toBe(false)
  })
})

describe('forgotPasswordSchema', () => {
  it('accepts a well-formed email', () => {
    expect(forgotPasswordSchema.safeParse({ email: 'ada@example.com' }).success).toBe(true)
  })

  it('refuses a malformed email', () => {
    expect(forgotPasswordSchema.safeParse({ email: 'not-an-email' }).success).toBe(false)
  })
})

describe('resetPasswordSchema', () => {
  it('accepts two matching, strong passwords', () => {
    const result = resetPasswordSchema.safeParse({
      newPassword: 'Passw0rd',
      confirmPassword: 'Passw0rd',
    })

    expect(result.success).toBe(true)
  })

  it('refuses passwords that do not match, on the confirmation field', () => {
    const result = resetPasswordSchema.safeParse({
      newPassword: 'Passw0rd',
      confirmPassword: 'Different1',
    })

    expect(result.success).toBe(false)
    if (!result.success) {
      expect(result.error.issues[0].path).toEqual(['confirmPassword'])
    }
  })

  it('refuses a weak new password even when the confirmation matches it', () => {
    const result = resetPasswordSchema.safeParse({
      newPassword: 'weak',
      confirmPassword: 'weak',
    })

    expect(result.success).toBe(false)
  })
})

describe('profileSchema', () => {
  const valid = { fullName: 'Ada Perera', phoneNumber: '', contactAddress: '' }

  it('accepts a name with a blank phone number and address', () => {
    expect(profileSchema.safeParse(valid).success).toBe(true)
  })

  it('accepts a phone number with at least 7 digits and the allowed punctuation', () => {
    expect(profileSchema.safeParse({ ...valid, phoneNumber: '+94 77 123 4567' }).success).toBe(true)
  })

  it('refuses a phone number with fewer than 7 digits', () => {
    expect(profileSchema.safeParse({ ...valid, phoneNumber: '12345' }).success).toBe(false)
  })

  it('refuses a phone number with letters', () => {
    expect(profileSchema.safeParse({ ...valid, phoneNumber: 'call-me-please' }).success).toBe(false)
  })

  it('refuses a contact address over 255 characters', () => {
    expect(profileSchema.safeParse({ ...valid, contactAddress: 'x'.repeat(256) }).success).toBe(false)
  })
})

describe('adminUserSchema', () => {
  it('accepts Admin — unlike registerSchema, an administrator may grant it', () => {
    const result = adminUserSchema.safeParse({
      fullName: 'Ada Perera',
      email: 'ada@example.com',
      role: 'Admin',
    })

    expect(result.success).toBe(true)
  })

  it('refuses a role that is not a platform role', () => {
    const result = adminUserSchema.safeParse({
      fullName: 'Ada Perera',
      email: 'ada@example.com',
      role: 'SuperAdmin',
    })

    expect(result.success).toBe(false)
  })
})
