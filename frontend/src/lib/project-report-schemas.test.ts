import { describe, expect, it } from 'vitest'

import {
  EMPTY_PROJECT_REPORT_FILTER,
  projectReportFilterSchema,
  type ProjectReportFilterValues,
} from './project-report-schemas'

/**
 * The project status report's filter form (US-18 AC-2), tested at the schema —
 * the boundary cases a page-level render would not exercise one by one.
 */
function parse(overrides: Partial<ProjectReportFilterValues> = {}) {
  return projectReportFilterSchema.safeParse({ ...EMPTY_PROJECT_REPORT_FILTER, ...overrides })
}

function messageFor(overrides: Partial<ProjectReportFilterValues>, field: string) {
  const result = parse(overrides)

  return result.success
    ? undefined
    : result.error.issues.find((issue) => issue.path.includes(field))?.message
}

describe('projectReportFilterSchema', () => {
  it('accepts no filter at all — the whole pipeline', () => {
    expect(parse().success).toBe(true)
  })

  it('accepts any subset of the project statuses', () => {
    expect(parse({ statuses: ['Pending'] }).success).toBe(true)
    expect(parse({ statuses: ['Designing', 'DesignApproved', 'Cancelled'] }).success).toBe(true)
  })

  it('refuses a status the service does not have', () => {
    const result = projectReportFilterSchema.safeParse({
      ...EMPTY_PROJECT_REPORT_FILTER,
      statuses: ['Finished'],
    })

    expect(result.success).toBe(false)
  })

  it('refuses a status in the wrong case — the wire names are exact', () => {
    expect(
      projectReportFilterSchema.safeParse({ ...EMPTY_PROJECT_REPORT_FILTER, statuses: ['pending'] })
        .success,
    ).toBe(false)
  })

  it('accepts a full range, and a range of a single day', () => {
    expect(parse({ from: '2026-09-01', to: '2026-09-30' }).success).toBe(true)
    expect(parse({ from: '2026-09-15', to: '2026-09-15' }).success).toBe(true)
  })

  it('accepts a range open at either end', () => {
    expect(parse({ from: '2026-09-01' }).success).toBe(true)
    expect(parse({ to: '2026-09-30' }).success).toBe(true)
  })

  it('refuses a range that ends before it starts, and puts the message on the end date', () => {
    expect(parse({ from: '2026-09-30', to: '2026-09-01' }).success).toBe(false)
    expect(messageFor({ from: '2026-09-30', to: '2026-09-01' }, 'to')).toBe(
      'The end date must not be before the start date.',
    )
  })

  it('compares days across a month and a year boundary', () => {
    expect(parse({ from: '2026-12-31', to: '2027-01-01' }).success).toBe(true)
    expect(parse({ from: '2027-01-01', to: '2026-12-31' }).success).toBe(false)
  })

  it.each(['2026-02-31', '2026-13-01', '2026-00-10', '2026-9-1', '01/09/2026', 'yesterday'])(
    'refuses %s as a date',
    (value) => {
      expect(messageFor({ from: value }, 'from')).toBe('Enter a valid date.')
      expect(messageFor({ to: value }, 'to')).toBe('Enter a valid date.')
    },
  )

  it('accepts 29 February only in a leap year', () => {
    expect(parse({ from: '2028-02-29' }).success).toBe(true)
    expect(parse({ from: '2027-02-29' }).success).toBe(false)
  })
})
