import { describe, expect, it } from 'vitest'

import { newProjectSchema } from './project-schemas'

describe('newProjectSchema', () => {
  const valid = {
    name: 'Perera Family Home',
    location: 'Colombo',
    landSizePerches: 20.5,
    budget: 5_000_000,
    floors: 2,
    bedrooms: 3,
    bathrooms: 2,
    garageSpaces: 1,
    otherRequirements: '',
  }

  it('accepts a well-formed submission', () => {
    expect(newProjectSchema.safeParse(valid).success).toBe(true)
  })

  it('refuses a project name shorter than 3 characters', () => {
    expect(newProjectSchema.safeParse({ ...valid, name: 'Hi' }).success).toBe(false)
  })

  it('refuses a location shorter than 2 characters', () => {
    expect(newProjectSchema.safeParse({ ...valid, location: 'X' }).success).toBe(false)
  })

  it('refuses a land size of zero', () => {
    expect(newProjectSchema.safeParse({ ...valid, landSizePerches: 0 }).success).toBe(false)
  })

  it('refuses a land size larger than the DECIMAL(10,2) column can hold', () => {
    expect(newProjectSchema.safeParse({ ...valid, landSizePerches: 100_000_000 }).success).toBe(false)
  })

  it('refuses a budget of zero', () => {
    expect(newProjectSchema.safeParse({ ...valid, budget: 0 }).success).toBe(false)
  })

  it('accepts zero bedrooms, bathrooms and garage spaces — not every build is a house', () => {
    const result = newProjectSchema.safeParse({ ...valid, bedrooms: 0, bathrooms: 0, garageSpaces: 0 })

    expect(result.success).toBe(true)
  })

  it('refuses zero floors — a building with no floors is not a building', () => {
    expect(newProjectSchema.safeParse({ ...valid, floors: 0 }).success).toBe(false)
  })

  it('refuses a floor count over 100', () => {
    expect(newProjectSchema.safeParse({ ...valid, floors: 101 }).success).toBe(false)
  })

  it('refuses a fractional floor count', () => {
    expect(newProjectSchema.safeParse({ ...valid, floors: 1.5 }).success).toBe(false)
  })

  it('treats an empty number box (NaN) as "required", not a type error', () => {
    // valueAsNumber hands back NaN for an empty input; z.number() would
    // otherwise report a type mismatch, which reads badly to a person filling
    // in a form.
    const result = newProjectSchema.safeParse({ ...valid, floors: Number.NaN })

    expect(result.success).toBe(false)
    if (!result.success) {
      expect(result.error.issues.some((issue) => issue.message === 'Number of floors is required.')).toBe(true)
    }
  })

  it('accepts blank other requirements — the one optional field', () => {
    expect(newProjectSchema.safeParse({ ...valid, otherRequirements: '' }).success).toBe(true)
  })

  it('refuses other requirements over 2000 characters', () => {
    expect(newProjectSchema.safeParse({ ...valid, otherRequirements: 'x'.repeat(2001) }).success).toBe(false)
  })
})
