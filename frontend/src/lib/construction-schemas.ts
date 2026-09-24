import { z } from 'zod'

import { MILESTONE_STATUSES } from './construction-api'

/**
 * The create-milestone form: just a name. The project id is in the URL and
 * the initial status is server-decided — neither belongs in the payload.
 * Mirrors the Construction Service's own `CreateMilestoneRequest`
 * (`[Required]`, `[StringLength(150)]`), so most mistakes are caught before
 * a request is made. The service revalidates everything, so its answer is
 * still the one that counts.
 */
export const createMilestoneSchema = z.object({
  name: z
    .string()
    .trim()
    .min(1, 'A milestone name is required.')
    .max(150, 'Milestone name must not exceed 150 characters.'),
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