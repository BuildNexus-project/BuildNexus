import { z } from 'zod'

import { PROJECT_STATUSES } from './project-status'

/** A calendar day as a date input writes it: `yyyy-MM-dd`, or blank for "no limit". */
const optionalDay = z
  .string()
  .refine((value) => value === '' || isRealDay(value), 'Enter a valid date.')

/**
 * The project status report's filter form (US-18): which statuses to show and
 * the range of days projects were submitted in.
 *
 * Mirrors what the Project Service accepts on `GET /api/projects/reports/status`,
 * so most mistakes are caught before a request is made. The service revalidates
 * everything — an unknown status or a range that ends before it starts is a 400
 * there — so its answer is still the one that counts.
 *
 * An empty `statuses` means every status, not none: nobody opens a report to
 * see nothing, and "all" is what the checkboxes read as when none is ticked.
 * A blank `from` or `to` leaves that end of the range open.
 */
export const projectReportFilterSchema = z
  .object({
    statuses: z.array(z.enum(PROJECT_STATUSES, { message: 'Choose a valid project status.' })),
    from: optionalDay,
    to: optionalDay,
  })
  .refine((filter) => filter.from === '' || filter.to === '' || filter.from <= filter.to, {
    path: ['to'],
    message: 'The end date must not be before the start date.',
  })

export type ProjectReportFilterValues = z.infer<typeof projectReportFilterSchema>

/** No filtering at all: every status, no date limits. */
export const EMPTY_PROJECT_REPORT_FILTER: ProjectReportFilterValues = {
  statuses: [],
  from: '',
  to: '',
}

/**
 * Whether a string is a day that exists. The shape alone is not enough —
 * `2026-02-31` matches `yyyy-MM-dd` and is not a date — and the service would
 * refuse it, so refuse it here first.
 */
function isRealDay(value: string): boolean {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value)

  if (!match) {
    return false
  }

  const [year, month, day] = [Number(match[1]), Number(match[2]), Number(match[3])]
  const date = new Date(Date.UTC(year, month - 1, day))

  return (
    date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
  )
}
