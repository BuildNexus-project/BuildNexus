import { afterEach, describe, expect, it, vi } from 'vitest'

import { ApiError, apiFetch } from './api'
import {
  completeConstruction,
  fetchConstructionPhase,
  handOverConstruction,
  isStaleStateRefusal,
  startConstruction,
  type ConstructionPhase,
} from './construction-api'
import { apiResponse, stubFetch } from '@/test/fake-fetch'

/**
 * The build-phase half of the Construction Service client (US-14).
 *
 * These go through the real `apiFetch`, so the request shape, the problem-details
 * parsing and the `ApiError` mapping are all exercised rather than mocked past —
 * which is where the behaviour worth pinning lives: a 409 carrying a `reason` has
 * to reach the caller as something it can branch on, and a 404 from the phase read
 * is not an error at all.
 */
const projectId = '11111111-2222-4333-8444-555555555555'

/** The token-attaching fetch the auth context hands pages, standing in here as `apiFetch`. */
const authFetch = <T,>(path: string, options = {}) => apiFetch<T>(path, options)

const startedPhase: ConstructionPhase = {
  projectId,
  status: 'Started',
  startedAtUtc: '2026-03-02T10:00:00Z',
  completedAtUtc: null,
  handedOverAtUtc: null,
  handedOverByUserId: null,
  updatedAtUtc: '2026-03-02T10:00:00Z',
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('fetchConstructionPhase', () => {
  it('reads the phase for a project whose build is under way', async () => {
    const requests = stubFetch(apiResponse(200, startedPhase))

    const phase = await fetchConstructionPhase(authFetch, projectId)

    expect(phase).toEqual(startedPhase)
    expect(requests[0].path).toBe(`/api/construction/projects/${projectId}/phase`)
    // A read, so no method is set — apiFetch defaults to GET.
    expect(requests[0].method).toBeUndefined()
  })

  it('answers null when construction has not been started', async () => {
    // The service's 404 is a real state, not a failure: the PM's screen reads it
    // as "Start construction is the next step". Throwing here would make every
    // caller wrap the call in a try/catch to handle the normal case.
    stubFetch(apiResponse(404, { title: 'Construction has not started.' }))

    await expect(fetchConstructionPhase(authFetch, projectId)).resolves.toBeNull()
  })

  it('still throws on a failure that is not a missing phase', async () => {
    // A 403 means the caller is not a Project Manager, which is a real error and
    // must not be flattened into "no phase yet" — that would silently show the
    // start button to someone who may not press it.
    stubFetch(apiResponse(403, { title: 'Forbidden' }))

    await expect(fetchConstructionPhase(authFetch, projectId)).rejects.toBeInstanceOf(ApiError)
  })
})

describe('startConstruction', () => {
  it('posts to the start endpoint and returns the new phase', async () => {
    const requests = stubFetch(apiResponse(200, startedPhase))

    const phase = await startConstruction(authFetch, projectId)

    expect(phase.status).toBe('Started')
    expect(requests[0].path).toBe(`/api/construction/projects/${projectId}/start`)
    expect(requests[0].method).toBe('POST')
    // The project is in the URL and the service decides everything else, so there
    // is nothing to send.
    expect(requests[0].body).toBeUndefined()
  })

  it('surfaces the refused precondition as both prose and a reason', async () => {
    stubFetch(
      apiResponse(409, {
        title: 'No milestones defined.',
        detail: 'Define at least one construction milestone before starting the build.',
        reason: 'NoMilestonesDefined',
      }),
    )

    const error = await startConstruction(authFetch, projectId).catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(ApiError)
    const apiError = error as ApiError
    expect(apiError.status).toBe(409)
    // The sentence the PM reads, verbatim from the service.
    expect(apiError.detail).toContain('milestone')
    // And the half a caller can branch on without matching on prose.
    expect(apiError.reason).toBe('NoMilestonesDefined')
  })

  it('distinguishes an unapproved design from a missing milestone plan', async () => {
    stubFetch(
      apiResponse(409, {
        title: 'Design not yet approved.',
        detail: 'Construction can only be started once this project’s design has been approved.',
        reason: 'DesignNotApproved',
      }),
    )

    const error = (await startConstruction(authFetch, projectId).catch(
      (caught: unknown) => caught,
    )) as ApiError

    expect(error.reason).toBe('DesignNotApproved')
    // Not a stale screen — the PM has something to chase before the build starts.
    expect(isStaleStateRefusal(error)).toBe(false)
  })
})

describe('completeConstruction', () => {
  it('posts to the complete endpoint and returns the completed phase', async () => {
    const requests = stubFetch(
      apiResponse(200, {
        ...startedPhase,
        status: 'Completed',
        completedAtUtc: '2026-09-14T16:45:00Z',
      }),
    )

    const phase = await completeConstruction(authFetch, projectId)

    expect(phase.status).toBe('Completed')
    expect(phase.completedAtUtc).toBe('2026-09-14T16:45:00Z')
    // Complete is not terminal — handover still follows.
    expect(phase.handedOverAtUtc).toBeNull()
    expect(requests[0].path).toBe(`/api/construction/projects/${projectId}/complete`)
    expect(requests[0].method).toBe('POST')
  })

  it('surfaces an unfinished milestone as its own reason', async () => {
    stubFetch(
      apiResponse(409, {
        title: 'Milestones are not all complete.',
        detail: 'Every milestone must be marked Completed before construction can be completed.',
        reason: 'MilestonesIncomplete',
      }),
    )

    const error = (await completeConstruction(authFetch, projectId).catch(
      (caught: unknown) => caught,
    )) as ApiError

    expect(error.reason).toBe('MilestonesIncomplete')
    expect(isStaleStateRefusal(error)).toBe(false)
  })

  it('surfaces completing a build that never started', async () => {
    // AC-3's named out-of-order case, as the client sees it.
    stubFetch(
      apiResponse(409, {
        title: 'Construction has not started.',
        detail: 'Start construction on this project first.',
        reason: 'NotStarted',
      }),
    )

    const error = (await completeConstruction(authFetch, projectId).catch(
      (caught: unknown) => caught,
    )) as ApiError

    expect(error.reason).toBe('NotStarted')
  })
})

describe('handOverConstruction', () => {
  const actingPm = '22222222-0000-4000-8000-000000000002'

  it('posts to the handover endpoint and returns the terminal phase', async () => {
    const requests = stubFetch(
      apiResponse(200, {
        ...startedPhase,
        status: 'HandedOver',
        completedAtUtc: '2026-09-14T16:45:00Z',
        handedOverAtUtc: '2026-09-20T11:00:00Z',
        handedOverByUserId: actingPm,
      }),
    )

    const phase = await handOverConstruction(authFetch, projectId)

    expect(phase.status).toBe('HandedOver')
    expect(phase.handedOverAtUtc).toBe('2026-09-20T11:00:00Z')
    // Handover raises no event, so the reply is where the actor surfaces.
    expect(phase.handedOverByUserId).toBe(actingPm)
    expect(requests[0].path).toBe(`/api/construction/projects/${projectId}/handover`)
    expect(requests[0].method).toBe('POST')
    expect(requests[0].body).toBeUndefined()
  })

  it('surfaces an unsettled final payment as its own reason', async () => {
    // The refusal to expect in practice until the Payment Service ships the
    // settlement event. It must read as "chase the invoice", not as a bug.
    stubFetch(
      apiResponse(409, {
        title: 'Final payment not settled.',
        detail: "This project's final payment has not been settled yet, so it cannot be handed over.",
        reason: 'FinalPaymentNotSettled',
      }),
    )

    const error = (await handOverConstruction(authFetch, projectId).catch(
      (caught: unknown) => caught,
    )) as ApiError

    expect(error.status).toBe(409)
    expect(error.reason).toBe('FinalPaymentNotSettled')
    expect(error.detail).toContain('final payment')
    // Something for the PM to chase, not a stale screen to re-read.
    expect(isStaleStateRefusal(error)).toBe(false)
  })

  it('surfaces handing over a build that is not complete', async () => {
    stubFetch(
      apiResponse(409, {
        title: 'Construction is not complete.',
        detail: 'Mark construction complete before handing the project over to the client.',
        reason: 'NotCompleted',
      }),
    )

    const error = (await handOverConstruction(authFetch, projectId).catch(
      (caught: unknown) => caught,
    )) as ApiError

    expect(error.reason).toBe('NotCompleted')
  })
})

describe('isStaleStateRefusal', () => {
  it.each(['AlreadyStarted', 'AlreadyCompleted'])(
    'treats %s as a stale screen rather than something for the PM to fix',
    (reason) => {
      // The transition had already happened — another PM did it, or this tab has
      // been open a while. Re-reading the phase is the right response, not asking
      // the user to correct anything.
      expect(isStaleStateRefusal(new ApiError(409, { reason }))).toBe(true)
    },
  )

  it.each(['DesignNotApproved', 'NoMilestonesDefined', 'NotStarted', 'MilestonesIncomplete'])(
    'treats %s as something the PM has to act on',
    (reason) => {
      expect(isStaleStateRefusal(new ApiError(409, { reason }))).toBe(false)
    },
  )

  it('is false for a refusal that carried no reason at all', () => {
    // An older service build, or a failure raised before the endpoint ran. The
    // safe default is "not stale": re-reading on every unexplained 409 could loop.
    expect(isStaleStateRefusal(new ApiError(409, { title: 'Conflict' }))).toBe(false)
  })

  it('is false for something that is not an ApiError', () => {
    // A dropped connection has no reason to read.
    expect(isStaleStateRefusal(new Error('network down'))).toBe(false)
  })
})
