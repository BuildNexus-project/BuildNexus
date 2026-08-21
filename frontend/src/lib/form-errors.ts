import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'

import { ApiError } from './api'

/**
 * Moves a failed request's messages onto the form.
 *
 * Validation failures come back keyed by the server's property names
 * (`Email`, `Password`), which are the same fields lower-cased on this side, so
 * each message lands on the input that caused it. Anything without a field —
 * a duplicate email, a 500 — is returned for the caller to show at form level.
 */
export function applyApiErrorToForm<T extends FieldValues>(
  error: unknown,
  setError: UseFormSetError<T>,
  fallbackMessage: string,
): string | null {
  if (!(error instanceof ApiError)) {
    return fallbackMessage
  }

  const entries = Object.entries(error.fieldErrors)

  if (entries.length > 0) {
    for (const [property, messages] of entries) {
      const field = (property.charAt(0).toLowerCase() + property.slice(1)) as Path<T>
      setError(field, { type: 'server', message: messages.join(' ') })
    }

    return null
  }

  return error.detail ?? error.title ?? fallbackMessage
}
