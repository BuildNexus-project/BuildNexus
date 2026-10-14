import { z } from 'zod'

import { MILESTONE_STATUSES } from './construction-api'
import { isRealDay } from './milestone-dates'

/**
 * The create-milestone form: a name, and optionally the day it should be finished by. The
 * project id is in the URL and the initial status is server-decided — neither belongs in the
 * payload. Mirrors the Construction Service's own `CreateMilestoneRequest` (`[Required]`,
 * `[StringLength(150)]`, an optional date), so most mistakes are caught before a request is
 * made. The service revalidates everything, so its answer is still the one that counts.
 *
 * The due date arrives from a date input as `yyyy-MM-dd`, or an empty string when the field
 * was left alone — which means "no date", not "an invalid one".
 */
export const createMilestoneSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'A milestone name is required.')
    .max(150, 'Milestone name must not exceed 150 characters.'),
  dueDate: z
    .string()
    .optional()
    .refine((value) => value === undefined || value === '' || isRealDay(value), {
      message: 'Enter a real date, as day, month and year.',
    }),
})

export type CreateMilestoneValues = z.infer<typeof createMilestoneSchema>

/**
 * The update-status form: one of the three states (US-12 AC-2). `z.enum`
 * over `MILESTONE_STATUSES` refuses anything outside `NotStarted`,
 * `InProgress` or `Completed` before the request is made — the service's own
 * enum binding does the same, but catching it at the form edge means the PM
 * sees the message beside the field rather than a round-tripped 400.
 */
export const updateMilestoneStatusSchema = z.object({
  status: z.enum(MILESTONE_STATUSES, {
    message: 'Choose Not Started, In Progress or Completed.',
  }),
})

export type UpdateMilestoneStatusValues = z.infer<typeof updateMilestoneStatusSchema>