import { afterEach, describe, expect, it, vi } from 'vitest'

import { ApiError, apiFetch } from './api'
import {
  daysSinceUpdate,
  fetchProjectOversight,
  staffLabel,
  type ProjectOversight,
} from './oversight-api'
import { apiResponse, stubFetch } from '@/test/fake-fetch'

/**
 * The oversight client (US-38). These go through the real `apiFetch`, so the
 * request shape and the problem-details parsing are exercised rather than
 * mocked past.
 */
const authFetch = <T,>(path: string, options = {}) => apiFetch<T>(path, options)

const oversight: ProjectOversight = {
  generatedAt: '2026-10-03T08:00:00Z',
  stalledAfterDays: 14,
  totalProjects: 1,
  stalledCount: 0,
  projects: [
    {
      id: '11111111-2222-4333-8444-555555555555',
      name: 'Beachfront villa',
      location: 'Galle',
      status: 'Designing',
      assignedArchitectId: 'a1',
      assignedArchitectName: 'Amara Perera',
      assignedProjectManagerId: null,
      assignedProjectManagerName: null,
      createdAt: '2026-09-02T09:00:00Z',
      updatedAt: '2026-09-30T09:00:00Z',
      isStalled: false,
    },
  ],
}

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('fetchProjectOversight', () => {
  it('reads the oversight route with a plain GET', async () => {
    const requests = stubFetch(apiResponse(200, oversight))

    const result = await fetchProjectOversight(authFetch)

    expect(requests[0].path).toBe('/api/projects/oversight')
    expect(requests[0].method).toBeUndefined()
    expect(result).toEqual(oversight)
  })

  it('surfaces a non-Admin caller as a 403', async () => {
    stubFetch(apiResponse(403, { title: 'Forbidden' }))

    const error = await fetchProjectOversight(authFetch).catch((caught: unknown) => caught)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(403)
  })
})

describe('staffLabel', () => {
  it('is the name when there is one', () => {
    expect(staffLabel('Amara Perera', 'a1')).toBe('Amara Perera')
  })

  it('says nobody is assigned when there is no id', () => {
    expect(staffLabel(null, null)).toBe('Not assigned')
  })

  it('does not read as empty when somebody is assigned but their name is unknown', () => {
    expect(staffLabel(null, 'a1')).toBe('Assigned (name unavailable)')
  })
})

describe('daysSinceUpdate', () => {
  const now = new Date('2026-10-03T12:00:00Z')

  it('counts whole days', () => {
    expect(daysSinceUpdate('2026-09-19T12:00:00Z', now)).toBe(14)
    expect(daysSinceUpdate('2026-09-19T13:00:00Z', now)).toBe(13)
  })

  it('is zero for something updated today', () => {
    expect(daysSinceUpdate('2026-10-03T08:00:00Z', now)).toBe(0)
  })

  it('is never negative, even if the service clock is ahead', () => {
    expect(daysSinceUpdate('2026-10-04T12:00:00Z', now)).toBe(0)
  })
})
