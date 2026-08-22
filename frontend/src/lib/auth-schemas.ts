import { z } from 'zod'

import { SELECTABLE_ROLES } from './roles'

/**
 * Mirrors the User Service's own registration rules, so most mistakes are
 * caught before a request is made. The service revalidates everything.
 */
export const registerSchema = z.object({
  fullName: z
    .string()
    .trim()
    .min(2, 'Full name must be at least 2 characters.')
    .max(150, 'Full name must not exceed 150 characters.'),
  email: z.email('Enter a valid email address.').max(255, 'Email must not exceed 255 characters.'),
  password: z
    .string()
    .min(8, 'Password must be at least 8 characters.')
    .max(128, 'Password must not exceed 128 characters.')
    .regex(/(?=.*[A-Za-z])(?=.*\d)/, 'Password must contain at least one letter and one digit.'),
  role: z.enum(SELECTABLE_ROLES, 'Choose the role that describes you.'),
})

export type RegisterValues = z.infer<typeof registerSchema>

/**
 * Login deliberately checks presence only — no length or complexity rules.
 * Applying the registration rules here would let a caller tell a badly-formed
 * password apart from a wrong one, which is exactly what the service's single
 * generic error avoids.
 */
export const loginSchema = z.object({
  email: z.email('Enter a valid email address.'),
  password: z.string().min(1, 'Enter your password.'),
})

export type LoginValues = z.infer<typeof loginSchema>

/**
 * Mirrors the User Service's own profile rules, so most mistakes are caught
 * before a request is made. The service revalidates everything.
 *
 * Email and role are absent by design: neither is self-editable, so the form
 * shows them read-only rather than collecting a value the service would refuse.
 */
export const profileSchema = z.object({
  fullName: z
    .string()
    .trim()
    .min(2, 'Full name must be at least 2 characters.')
    .max(150, 'Full name must not exceed 150 characters.'),
  /**
   * Blank is allowed and clears the stored number. Anything else must carry at
   * least seven digits and nothing but digits, spaces, brackets, hyphens and a
   * leading + — the same pattern the service applies.
   */
  phoneNumber: z
    .string()
    .trim()
    .max(30, 'Phone number must not exceed 30 characters.')
    .regex(
      /^$|^(?=(?:[^0-9]*[0-9]){7,})\+?[0-9 ()-]+$/,
      'Phone number must contain at least 7 digits and may contain only digits, spaces, brackets, hyphens and a leading +.',
    ),
  /** Blank clears the stored address. */
  contactAddress: z.string().trim().max(255, 'Contact address must not exceed 255 characters.'),
})

export type ProfileValues = z.infer<typeof profileSchema>
