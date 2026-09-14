import { describe, expect, it, vi } from 'vitest'

import { ApiError } from './api'
import { applyApiErrorToForm } from './form-errors'

type Values = { email: string; password: string }

const FALLBACK = 'Something went wrong. Please try again.'

describe('applyApiErrorToForm', () => {
  it('returns the fallback message and touches no field for a non-ApiError', () => {
    const setError = vi.fn()

    const result = applyApiErrorToForm<Values>(new Error('network down'), setError, FALLBACK)

    expect(result).toBe(FALLBACK)
    expect(setError).not.toHaveBeenCalled()
  })

  it('sets each field error and returns null, so the caller shows nothing at form level', () => {
    const setError = vi.fn()
    const error = new ApiError(400, { fieldErrors: { Email: ['Email is required.'] } })

    const result = applyApiErrorToForm<Values>(error, setError, FALLBACK)

    expect(result).toBeNull()
    expect(setError).toHaveBeenCalledWith('email', { type: 'server', message: 'Email is required.' })
  })

  it('lower-cases only the first letter of the server field name', () => {
    // The server sends PascalCase ("Email"); react-hook-form fields are camelCase.
    const setError = vi.fn()
    const error = new ApiError(400, { fieldErrors: { Password: ['Too short.'] } })

    applyApiErrorToForm<Values>(error, setError, FALLBACK)

    expect(setError).toHaveBeenCalledWith('password', { type: 'server', message: 'Too short.' })
  })

  it('joins multiple messages for the same field with a space', () => {
    const setError = vi.fn()
    const error = new ApiError(400, {
      fieldErrors: { Password: ['Too short.', 'Needs a digit.'] },
    })

    applyApiErrorToForm<Values>(error, setError, FALLBACK)

    expect(setError).toHaveBeenCalledWith('password', {
      type: 'server',
      message: 'Too short. Needs a digit.',
    })
  })

  it('sets every field named, not just the first', () => {
    const setError = vi.fn()
    const error = new ApiError(400, {
      fieldErrors: { Email: ['Email is required.'], Password: ['Too short.'] },
    })

    applyApiErrorToForm<Values>(error, setError, FALLBACK)

    expect(setError).toHaveBeenCalledTimes(2)
  })

  it('returns the detail when the error names no field', () => {
    const setError = vi.fn()
    const error = new ApiError(409, { detail: 'That email is already registered.' })

    const result = applyApiErrorToForm<Values>(error, setError, FALLBACK)

    expect(result).toBe('That email is already registered.')
    expect(setError).not.toHaveBeenCalled()
  })

  it('falls back to the title when there is no detail', () => {
    const setError = vi.fn()
    const error = new ApiError(500, { title: 'Internal Server Error' })

    expect(applyApiErrorToForm<Values>(error, setError, FALLBACK)).toBe('Internal Server Error')
  })

  it('falls back to the caller-supplied message when the error has neither', () => {
    const setError = vi.fn()
    const error = new ApiError(500)

    expect(applyApiErrorToForm<Values>(error, setError, FALLBACK)).toBe(FALLBACK)
  })
})
