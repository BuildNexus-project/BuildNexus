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
    firstName: 'Ada',
    lastName: 'Perera',
    email: 'ada@example.com',
    phoneNumber: '0771234567',
    password: 'Passw0rd',
    confirmPassword: 'Passw0rd',
  }

  it('accepts a well-formed registration', () => {
    expect(registerSchema.safeParse(valid).success).toBe(true)
  })

  it.each([
    ['', 'Confirm your password.'],
    ['Different1', 'Passwords do not match.'],
  ])('requires a matching password confirmation (%s)', (confirmPassword, message) => {
    const result = registerSchema.safeParse({ ...valid, confirmPassword })
    expect(result.success).toBe(false)
    if (!result.success) {
      expect(result.error.issues[0].path).toEqual(['confirmPassword'])
      expect(result.error.issues[0].message).toBe(message)
    }
  })

  it.each(['0771234567', '0712345678', '+94771234567', ' +94771234567 '])('accepts a valid phone number (%s)', (phoneNumber) => {
    expect(registerSchema.parse({ ...valid, phoneNumber }).phoneNumber).toBe(phoneNumber.trim())
  })

  it.each([undefined, null, '', '   '])('requires a phone number (%s)', (phoneNumber) => {
    const result = registerSchema.safeParse({ ...valid, phoneNumber })
    expect(result.success).toBe(false)
    if (!result.success) {
      expect(result.error.issues[0].path).toEqual(['phoneNumber'])
    }
  })

  it.each([
    '12345', 'call me', '077123456', '07712345678', '0171234567',
    '+9477123456', '+947712345678', '+94112345678', '+940771234567',
    '94771234567', '+94 77 123 4567', '077-123-4567', '(077)1234567',
    '077-123-4567 ext 8', '1'.repeat(31),
  ])('refuses an invalid phone number (%s)', (phoneNumber) => {
    const result = registerSchema.safeParse({ ...valid, phoneNumber })
    expect(result.success).toBe(false)
    if (!result.success) {
      expect(result.error.issues[0].path).toEqual(['phoneNumber'])
    }
  })

  it('refuses a password shorter than 8 characters', () => {
    const result = registerSchema.safeParse({ ...valid, password: 'Pw0rd1', confirmPassword: 'Pw0rd1' })

    expect(result.success).toBe(false)
  })

  it('refuses a password with no digit', () => {
    const result = registerSchema.safeParse({ ...valid, password: 'NoDigitsHere', confirmPassword: 'NoDigitsHere' })

    expect(result.success).toBe(false)
  })

  it('refuses a password with no letter', () => {
    const result = registerSchema.safeParse({ ...valid, password: '12345678', confirmPassword: '12345678' })

    expect(result.success).toBe(false)
  })

  it.each(['firstName', 'lastName'] as const)('requires %s', (field) => {
    const result = registerSchema.safeParse({ ...valid, [field]: '   ' })
    expect(result.success).toBe(false)
    if (!result.success) expect(result.error.issues[0].path).toEqual([field])
  })

  it('trims both name fields before saving', () => {
    expect(registerSchema.parse({ ...valid, firstName: ' Ada ', lastName: ' Perera ' })).toEqual(valid)
  })

  it('respects the stored full name limit including the separating space', () => {
    expect(registerSchema.safeParse({ ...valid, firstName: 'a'.repeat(75), lastName: 'b'.repeat(74) }).success).toBe(true)
    expect(registerSchema.safeParse({ ...valid, firstName: 'a'.repeat(75), lastName: 'b'.repeat(75) }).success).toBe(false)
  })

  it('refuses a malformed email', () => {
    expect(registerSchema.safeParse({ ...valid, email: 'not-an-email' }).success).toBe(false)
  })

  it.each(['Architect', 'ProjectManager', 'Admin'])('does not include a supplied %s role in registration', (role) => {
    expect(registerSchema.parse({ ...valid, role })).toEqual(valid)
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
      firstName: 'Ada', lastName: 'Perera', email: 'ada@example.com', phoneNumber: '0771234567', password: 'short', confirmPassword: 'short',
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

  it.each(['0771234567', '+94771234567'])('accepts a mobile number (%s)', (phoneNumber) => {
    expect(profileSchema.safeParse({ ...valid, phoneNumber }).success).toBe(true)
  })

  it.each(['077123456', '07712345678', '0112345678', '+940771234567', '+94 77 123 4567'])('refuses a phone number with the wrong format (%s)', (phoneNumber) => {
    expect(profileSchema.safeParse({ ...valid, phoneNumber }).success).toBe(false)
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
