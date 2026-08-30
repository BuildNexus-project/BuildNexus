import { z } from 'zod'

/**
 * A whole-number requirement — floors, bedrooms, bathrooms, garage spaces.
 *
 * Number inputs registered with `valueAsNumber` hand back `NaN` when the box is
 * empty, and `z.number()` refuses that as a type error. The message is written
 * for that case rather than the impossible one, because "Bedrooms is required"
 * is what an empty box actually means to the person reading it.
 */
function requiredCount(label: string, min: number, max: number) {
  return z
    .number(`${label} is required.`)
    .int(`${label} must be a whole number.`)
    .min(min, `${label} must be between ${min} and ${max}.`)
    .max(max, `${label} must be between ${min} and ${max}.`)
}

/**
 * Mirrors the Project Service's own rules for a new project, so most mistakes
 * are caught before a request is made. The service revalidates everything, and
 * its answer is the one that counts.
 *
 * The upper bounds are the widths of the columns behind them —
 * `DECIMAL(10,2)` for land size and `DECIMAL(15,2)` for budget — so a number
 * too large to store is refused here rather than at the database.
 */
export const newProjectSchema = z.object({
  name: z
    .string()
    .trim()
    .min(3, 'Project name must be at least 3 characters.')
    .max(150, 'Project name must not exceed 150 characters.'),
  location: z
    .string()
    .trim()
    .min(2, 'Location must be at least 2 characters.')
    .max(255, 'Location must not exceed 255 characters.'),
  /** Perches, and fractional — a plot is rarely a whole number of them. */
  landSizePerches: z
    .number('Land size is required.')
    .gt(0, 'Land size must be greater than 0 perches.')
    .max(99999999.99, 'Land size is larger than we can record.'),
  budget: z
    .number('Budget is required.')
    .gt(0, 'Budget must be greater than 0.')
    .max(9999999999999.99, 'Budget is larger than we can record.'),
  /** At least one — a building with no floors is not a building. */
  floors: requiredCount('Number of floors', 1, 100),
  /** Zero is allowed here and below: not every build is a house. */
  bedrooms: requiredCount('Number of bedrooms', 0, 100),
  bathrooms: requiredCount('Number of bathrooms', 0, 100),
  garageSpaces: requiredCount('Number of garage spaces', 0, 20),
  /**
   * The one optional field. Blank is allowed and stored as nothing, which is
   * also what a project submitted without it has.
   */
  otherRequirements: z
    .string()
    .trim()
    .max(2000, 'Other requirements must not exceed 2000 characters.'),
})

export type NewProjectValues = z.infer<typeof newProjectSchema>
