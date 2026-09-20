import { describe, expect, it } from 'vitest'

import { createMilestoneSchema, updateMilestoneStatusSchema } from './construction-schemas'

describe('createMilestoneSchema', () => {
  it('accepts a non-empty name', () => {
    expect(createMilestoneSchema.safeParse({ name: 'Foundation poured' }).success).toBe(true)
  })

  it('refuses a blank name — the Construction Service refuses it too', () => {
    expect(createMilestoneSchema.safeParse({ name: '' }).success).toBe(false)
  })

  it('refuses a name that is only whitespace', () => {
    // .trim() runs before .min(1), so a name of only spaces fails the same
    // way a blank does — a caller who types "   " and one who types nothing
    // land on the same message.
    expect(createMilestoneSchema.safeParse({ name: '   ' }).success).toBe(false)
  })

  it('trims the name before it reaches the API', () => {
    const parsed = createMilestoneSchema.parse({ name: '  Foundation poured  ' })

    expect(parsed.name).toBe('Foundation poured')
  })

  it('accepts a name of exactly 150 characters — the service allows it', () => {
    expect(createMilestoneSchema.safeParse({ name: 'x'.repeat(150) }).success).toBe(true)
  })

  it('refuses a name over 150 characters — mirrors [StringLength(150)] on the service', () => {
    expect(createMilestoneSchema.safeParse({ name: 'x'.repeat(151) }).success).toBe(false)
  })
})

describe('updateMilestoneStatusSchema', () => {
  it('accepts NotStarted', () => {
    expect(updateMilestoneStatusSchema.safeParse({ status: 'NotStarted' }).success).toBe(true)
  })

  it('accepts InProgress', () => {
    expect(updateMilestoneStatusSchema.safeParse({ status: 'InProgress' }).success).toBe(true)
  })

  it('accepts Completed', () => {
    expect(updateMilestoneStatusSchema.safeParse({ status: 'Completed' }).success).toBe(true)
  })

  it('refuses a fourth state — the service enforces the same three-value domain', () => {
    // Pinned here so a well-meaning "Blocked" addition on the frontend
    // that has not yet reached the enum on the service is caught client-side.
    expect(updateMilestoneStatusSchema.safeParse({ status: 'Blocked' }).success).toBe(false)
  })

  it('refuses a case-mismatched value — the wire format is the exact enum name', () => {
    // The service uses JsonStringEnumConverter with the default case-sensitive
    // matching, so "completed" would be a 400 there too — catching it here
    // means the PM sees the message beside the field.
    expect(updateMilestoneStatusSchema.safeParse({ status: 'completed' }).success).toBe(false)
  })

  it('refuses a missing status', () => {
    expect(updateMilestoneStatusSchema.safeParse({}).success).toBe(false)
  })
})