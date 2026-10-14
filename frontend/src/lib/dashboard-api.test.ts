import { afterEach, describe, expect, it, vi } from 'vitest'

import { ApiError, apiFetch } from './api'
import {
  DESIGN_STATES,
  DESIGN_STATE_LABELS,
  fetchAdminProjectsDashboard,
  fetchAdminUsersDashboard,
  fetchArchitectDesignDashboard,
  fetchArchitectProjectsDashboard,
  fetchClientConstructionDashboard,
  fetchClientDesignDashboard,
  fetchClientPaymentsDashboard,
  fetchClientProjectsDashboard,
  fetchProjectManagerConstructionDashboard,
} from './dashboard-api'
import { apiResponse, stubFetch } from '@/test/fake-fetch'

/**
 * The role-dashboard clients (US-21). These go through the real `apiFetch`, so the
 * request shape, the problem-details parsing and the `ApiError` mapping are exercised
 * rather than mocked past.
 *
 * There is one endpoint per role per service, so most of what is worth pinning is the
 * path: a typo here is a 404 that only shows up in front of a user of that role.
 */
const authFetch = <T,>(path: string, options = {}) => apiFetch<T>(path, options)

afterEach(() => {
  vi.unstubAllGlobals()
})

/** Every slice, with the path its service serves it on. */
const SLICES = [
  ['project service, client', fetchClientProjectsDashboard, '/api/projects/dashboard/client'],
  ['project service, architect', fetchArchitectProjectsDashboard, '/api/projects/dashboard/architect'],
  ['project service, admin', fetchAdminProjectsDashboard, '/api/projects/dashboard/admin'],
  ['user service, admin', fetchAdminUsersDashboard, '/api/users/dashboard/admin'],
  ['design service, client', fetchClientDesignDashboard, '/api/designs/dashboard/client'],
  ['design service, architect', fetchArchitectDesignDashboard, '/api/designs/dashboard/architect'],
  ['construction service, client', fetchClientConstructionDashboard, '/api/construction/dashboard/client'],
  [
    'construction service, project manager',
    fetchProjectManagerConstructionDashboard,
    '/api/construction/dashboard/project-manager',
  ],
  ['payment service, client', fetchClientPaymentsDashboard, '/api/payments/dashboard/client'],
] as const

describe.each(SLICES)('the %s dashboard slice', (_name, fetchSlice, path) => {
  it('reads the slice from its own service and returns the body as sent', async () => {
    const body = { some: 'slice' }
    const requests = stubFetch(apiResponse(200, body))

    const result = await fetchSlice(authFetch)

    expect(result).toEqual(body)
    expect(requests).toHaveLength(1)
    expect(requests[0].path).toBe(path)
    // A read, so no method is set — apiFetch defaults to GET — and no body is sent.
    expect(requests[0].method).toBeUndefined()
    expect(requests[0].body).toBeUndefined()
  })

  it('carries a refusal to the caller as an ApiError rather than swallowing it', async () => {
    stubFetch(apiResponse(403, { title: 'Forbidden' }))

    await expect(fetchSlice(authFetch)).rejects.toMatchObject({
      name: 'ApiError',
      status: 403,
    })
  })
})

describe('a slice whose service cannot answer', () => {
  it('reports the 502 with the reason the service gave, so the page can say why', async () => {
    // The Design Service asks the Project Service which projects the caller may see. When
    // it cannot, it answers 502 rather than an empty list — an outage must not read as a
    // Client with no projects — and the reason has to reach the page intact.
    stubFetch(
      apiResponse(502, {
        title: 'Projects could not be listed',
        detail: 'Your projects could not be looked up right now.',
      }),
    )

    const failure = await fetchClientDesignDashboard(authFetch).catch((error: unknown) => error)

    expect(failure).toBeInstanceOf(ApiError)
    expect((failure as ApiError).status).toBe(502)
    expect((failure as ApiError).detail).toBe('Your projects could not be looked up right now.')
  })
})

describe('the design states', () => {
  it('are listed with the state most in need of the Client first', () => {
    expect(DESIGN_STATES).toEqual(['NoDesign', 'AwaitingReview', 'RevisionRequested', 'Approved'])
  })

  it('each have a label to show in place of the wire value', () => {
    for (const state of DESIGN_STATES) {
      expect(DESIGN_STATE_LABELS[state]).toMatch(/\S/)
      // A wire value such as `AwaitingReview` runs its words together, which reads badly
      // in a UI — so no label may. (`Approved` is a single word and is rightly its own label.)
      expect(DESIGN_STATE_LABELS[state]).not.toMatch(/[a-z][A-Z]/)
    }
  })
})
