import { afterEach, describe, expect, it, vi } from 'vitest'

import { ApiError, apiFetch } from './api'
import {
  paymentRefusal,
  recordPayment,
  fetchMyProjectInvoices,
  fetchMyProjectQuotations,
  fetchProjectInvoices,
  fetchProjectQuotations,
  generateInvoice,
  generateQuotation,
  type Invoice,
  type Quotation,
} from './payment-api'
import { apiResponse, stubFetch } from '@/test/fake-fetch'

/**
 * The Payment Service client (US-15).
 *
 * These go through the real `apiFetch`, so the request shape and the
 * problem-details parsing are exercised rather than mocked past. The two routes
 * that matter most here are the staff one and the Client one: they are
 * deliberately different paths, because the Client's is ownership-scoped by the
 * service and the staff one is not.
 */
const projectId = '11111111-2222-4333-8444-555555555555'

/** The token-attaching fetch the auth context hands pages, standing in here as `apiFetch`. */
const authFetch = <T,>(path: string, options = {}) => apiFetch<T>(path, options)

const quotation: Quotation = {
  id: '99999999-2222-4333-8444-555555555555',
  projectId,
  estimatedTotal: 1_250_000.5,
  createdBy: '33333333-2222-4333-8444-555555555555',
  createdAtUtc: '2026-03-02T10:00:00Z',
}

const invoice: Invoice = {
  id: '88888888-2222-4333-8444-555555555555',
  projectId,
  amount: 250_000.75,
  status: 'Pending',
  createdBy: '33333333-2222-4333-8444-555555555555',
  createdAtUtc: '2026-03-02T10:00:00Z',
  paidAtUtc: null,
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('generateQuotation', () => {
  it('posts the estimated total to the project it is for', async () => {
    const requests = stubFetch(apiResponse(201, quotation))

    const created = await generateQuotation(authFetch, projectId, { estimatedTotal: 1_250_000.5 })

    expect(requests[0].path).toBe(`/api/payments/projects/${projectId}/quotations`)
    expect(requests[0].method).toBe('POST')
    expect(requests[0].body).toEqual({ estimatedTotal: 1_250_000.5 })
    expect(created.estimatedTotal).toBe(1_250_000.5)
  })

  it('surfaces a refused amount as an ApiError the form can read', async () => {
    stubFetch(
      apiResponse(400, {
        title: 'One or more validation errors occurred.',
        errors: { EstimatedTotal: ['The estimated total must be greater than zero.'] },
      }),
    )

    const error = await generateQuotation(authFetch, projectId, {
      estimatedTotal: 0,
    }).catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(400)
    expect((error as ApiError).fieldErrors.EstimatedTotal).toEqual([
      'The estimated total must be greater than zero.',
    ])
  })
})

describe('fetchProjectQuotations', () => {
  it('reads a project&apos;s quotations for staff', async () => {
    const requests = stubFetch(apiResponse(200, [quotation]))

    const quotations = await fetchProjectQuotations(authFetch, projectId)

    expect(requests[0].path).toBe(`/api/payments/projects/${projectId}/quotations`)
    expect(requests[0].method).toBeUndefined()
    expect(quotations).toHaveLength(1)
  })

  it('treats a project that has never been quoted as an empty list, not a failure', async () => {
    stubFetch(apiResponse(200, []))

    await expect(fetchProjectQuotations(authFetch, projectId)).resolves.toEqual([])
  })
})

describe('generateInvoice', () => {
  it('posts the amount and sends no status, so nothing can be created already paid', async () => {
    const requests = stubFetch(apiResponse(201, invoice))

    const created = await generateInvoice(authFetch, projectId, { amount: 250_000.75 })

    expect(requests[0].path).toBe(`/api/payments/projects/${projectId}/invoices`)
    expect(requests[0].method).toBe('POST')
    expect(requests[0].body).toEqual({ amount: 250_000.75 })
    expect(requests[0].body).not.toHaveProperty('status')
    expect(created.status).toBe('Pending')
  })
})

describe('fetchProjectInvoices', () => {
  it('reads a project&apos;s invoices for staff', async () => {
    const requests = stubFetch(apiResponse(200, [invoice]))

    await fetchProjectInvoices(authFetch, projectId)

    expect(requests[0].path).toBe(`/api/payments/projects/${projectId}/invoices`)
  })
})

describe("the Client's own cost reads", () => {
  it('uses the ownership-scoped path, not the staff one', async () => {
    // Different routes on purpose: the service scopes /my-projects to the
    // caller's own projects, so a Client read must not go through the staff
    // path — which would earn a 403 and, worse, would be the wrong gate.
    const requests = stubFetch(apiResponse(200, [quotation]), apiResponse(200, [invoice]))

    await fetchMyProjectQuotations(authFetch, projectId)
    await fetchMyProjectInvoices(authFetch, projectId)

    expect(requests[0].path).toBe(`/api/payments/my-projects/${projectId}/quotations`)
    expect(requests[1].path).toBe(`/api/payments/my-projects/${projectId}/invoices`)
  })

  it("surfaces another client's project as a 403 with the service's reason", async () => {
    stubFetch(
      apiResponse(403, {
        title: 'Not your project.',
        detail: 'You can only view quotations for your own projects.',
      }),
    )

    const error = await fetchMyProjectQuotations(authFetch, projectId).catch(
      (caught: unknown) => caught,
    )

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(403)
    expect((error as ApiError).detail).toBe('You can only view quotations for your own projects.')
  })
})

describe('recordPayment', () => {
  it('posts the amount to the invoice it is against', async () => {
    const requests = stubFetch(
      apiResponse(201, {
        payment: {
          id: '77777777-2222-4333-8444-555555555555',
          invoiceId: invoice.id,
          amount: 400,
          paidBy: '33333333-2222-4333-8444-555555555555',
          recordedAtUtc: '2026-09-25T10:00:00Z',
        },
        outstandingAmount: 600,
        invoiceStatus: 'Pending',
      }),
    )

    const result = await recordPayment(authFetch, invoice.id, { amount: 400 })

    expect(requests[0].path).toBe(`/api/payments/invoices/${invoice.id}/payments`)
    expect(requests[0].method).toBe('POST')
    expect(requests[0].body).toEqual({ amount: 400 })
    expect(result.outstandingAmount).toBe(600)
    expect(result.invoiceStatus).toBe('Pending')
  })

  it('reports the invoice as Paid once a payment settles it', async () => {
    stubFetch(
      apiResponse(201, {
        payment: {
          id: '77777777-2222-4333-8444-555555555555',
          invoiceId: invoice.id,
          amount: 600,
          paidBy: '33333333-2222-4333-8444-555555555555',
          recordedAtUtc: '2026-09-25T10:00:00Z',
        },
        outstandingAmount: 0,
        invoiceStatus: 'Paid',
      }),
    )

    const result = await recordPayment(authFetch, invoice.id, { amount: 600 })

    expect(result.outstandingAmount).toBe(0)
    expect(result.invoiceStatus).toBe('Paid')
  })
})

describe('paymentRefusal', () => {
  it("carries the invoice's real outstanding amount off an over-payment", async () => {
    // The whole reason the service sends it: the screen can offer the figure
    // that would have worked instead of only reporting the error.
    stubFetch(
      apiResponse(409, {
        title: 'Payment exceeds the outstanding amount.',
        detail: 'This invoice has 600.00 outstanding. Enter that amount or less.',
        reason: 'ExceedsOutstanding',
        outstandingAmount: 600,
      }),
    )

    const error = await recordPayment(authFetch, invoice.id, { amount: 700 }).catch(
      (caught: unknown) => caught,
    )

    expect(paymentRefusal(error)).toEqual({ reason: 'ExceedsOutstanding', outstandingAmount: 600 })
  })

  it('recognises an already-settled invoice', async () => {
    stubFetch(
      apiResponse(409, {
        title: 'This invoice is already paid.',
        detail: 'Nothing is outstanding on this invoice.',
        reason: 'AlreadyPaid',
        outstandingAmount: 0,
      }),
    )

    const error = await recordPayment(authFetch, invoice.id, { amount: 10 }).catch(
      (caught: unknown) => caught,
    )

    expect(paymentRefusal(error)?.reason).toBe('AlreadyPaid')
  })

  it('is null for a failure the screen cannot act on', async () => {
    // A 403 is a refusal, but not one with a figure to offer — it falls through
    // to the ordinary error message.
    stubFetch(apiResponse(403, { title: 'Not your invoice.', detail: 'Not yours.' }))

    const error = await recordPayment(authFetch, invoice.id, { amount: 10 }).catch(
      (caught: unknown) => caught,
    )

    expect(paymentRefusal(error)).toBeNull()
  })

  it('branches on the reason rather than the prose', async () => {
    // So improving the wording cannot break the screen.
    stubFetch(
      apiResponse(409, {
        title: 'Completely different wording.',
        detail: 'Reworded entirely.',
        reason: 'ExceedsOutstanding',
        outstandingAmount: 250.5,
      }),
    )

    const error = await recordPayment(authFetch, invoice.id, { amount: 999 }).catch(
      (caught: unknown) => caught,
    )

    expect(paymentRefusal(error)).toEqual({
      reason: 'ExceedsOutstanding',
      outstandingAmount: 250.5,
    })
  })
})
